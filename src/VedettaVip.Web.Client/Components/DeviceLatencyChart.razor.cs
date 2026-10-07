// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Globalization;
using ApexCharts;
using Microsoft.AspNetCore.Components;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Web.Client.Components;

/// <summary>
/// Latenza e perdita di un device (GET /api/metrics/device): RTT medio (area) e massimo (linea) in ms, perdita %
/// (barre, asse destro 0–100). Statistiche del periodo: RTT medio, massimo, 95° percentile, perdita e disponibilità.
/// </summary>
public partial class DeviceLatencyChart
{
    [Parameter, EditorRequired] public Guid DeviceId { get; set; }

    private sealed record ChartPoint(long X, decimal? RttAvg, decimal? RttMax, decimal? LossPct);

    private readonly record struct Stats(double? RttAvg, double? RttMax, double? RttP95, double? LossPct);

    private readonly ApexChartOptions<ChartPoint> options = BuildOptions();
    private DeviceLatencySeriesDto? series;
    private List<ChartPoint>? points;
    private Stats stats;

    protected override object LoadKey => DeviceId;

    private bool HasData => points?.Any(p => p.RttAvg is not null || p.LossPct is not null) == true;

    protected override async Task LoadCoreAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        series = await Api.GetDeviceLatencyAsync(DeviceId, from, to, ct);
        points = series.Points.Select(p => new ChartPoint(
            p.Time.ToUnixTimeMilliseconds(), ToDecimal(p.RttAvg), ToDecimal(p.RttMax), ToDecimal(p.LossPct))).ToList();

        var rtt = series.Points.Where(p => p.RttAvg is not null).Select(p => p.RttAvg!.Value).ToList();
        var loss = series.Points.Where(p => p.LossPct is not null).Select(p => p.LossPct!.Value).ToList();
        stats = new Stats(
            rtt.Count > 0 ? rtt.Average() : null,
            series.Points.Max(p => p.RttMax),
            Percentile95(rtt),
            loss.Count > 0 ? loss.Average() : null);
    }

    private string? Info() => series is null ? null : $"risoluzione {ResolutionText(series.BucketSeconds, series.Source)}";

    private static string Ms(double? v) => v is { } x ? x.ToString(x < 10 ? "0.0" : "0", CultureInfo.CurrentCulture) + " ms" : "-";

    private static string Pct(double? v) => v is { } x ? x.ToString(x is > 0 and < 1 or > 99 and < 100 ? "0.00" : "0.0", CultureInfo.CurrentCulture) + " %" : "-";

    private static ApexChartOptions<ChartPoint> BuildOptions()
    {
        const string ms = "function (v) { return v === null || v === undefined ? '' : (v < 10 ? v.toFixed(1) : v.toFixed(0)) + ' ms'; }";
        return new()
        {
            Chart = new Chart { Animations = new Animations { Enabled = false }, Toolbar = new Toolbar { Show = false }, Zoom = new Zoom { Enabled = false } },
            Xaxis = new XAxis { Type = XAxisType.Datetime, Labels = new XAxisLabels { DatetimeUTC = false } },
            // Un asse per serie: "RTT max" condivide la scala di "RTT medio" (SeriesName) ed è nascosto; la perdita a destra
            Yaxis =
            [
                new YAxis { SeriesName = "RTT medio", Min = 0, Labels = new YAxisLabels { Formatter = ms } },
                new YAxis { SeriesName = "RTT medio", Show = false, Labels = new YAxisLabels { Formatter = ms } },
                new YAxis
                {
                    SeriesName = "Perdita", Opposite = true, Min = 0, Max = 100, TickAmount = 4,
                    Labels = new YAxisLabels { Formatter = "function (v) { return v === null || v === undefined ? '' : v.toFixed(0) + ' %'; }" }
                }
            ],
            Tooltip = new Tooltip { Shared = true, X = new TooltipX { Format = "dd/MM HH:mm" } },
            Stroke = new Stroke { Width = 2, Curve = Curve.Straight },
            DataLabels = new DataLabels { Enabled = false },
            Fill = new Fill { Opacity = 0.3 },
            Legend = new Legend { Position = LegendPosition.Top },
            Colors = ["#2e7dd7", "#9bbbe6", "#d62828"]
        };
    }
}
