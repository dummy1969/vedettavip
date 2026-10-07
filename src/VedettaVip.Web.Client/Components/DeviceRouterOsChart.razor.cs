// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Globalization;
using ApexCharts;
using Microsoft.AspNetCore.Components;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Web.Client.Components;

/// <summary>
/// CPU (media e picco), memoria usata e temperatura di un router RouterOS (GET /api/metrics/device/routeros).
/// Percentuali sull'asse sinistro 0–100, temperatura sull'asse destro.
/// </summary>
public partial class DeviceRouterOsChart
{
    [Parameter, EditorRequired] public Guid DeviceId { get; set; }

    private sealed record ChartPoint(long X, decimal? CpuAvg, decimal? CpuMax, decimal? Memory, decimal? Temperature);

    private readonly record struct Stats(double? CpuAvg, double? CpuMax, double? CpuP95, double? MemoryAvg, double? TemperatureAvg, double? TemperatureMax);

    private readonly ApexChartOptions<ChartPoint> options = BuildOptions();
    private DeviceRouterOsSeriesDto? series;
    private List<ChartPoint>? points;
    private Stats stats;

    protected override object LoadKey => DeviceId;

    private bool HasData => points?.Any(p => p.CpuAvg is not null || p.Memory is not null || p.Temperature is not null) == true;

    protected override async Task LoadCoreAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        series = await Api.GetDeviceRouterOsAsync(DeviceId, from, to, ct);
        points = series.Points.Select(p => new ChartPoint(p.Time.ToUnixTimeMilliseconds(),
            ToDecimal(p.CpuAvg), ToDecimal(p.CpuMax), ToDecimal(p.MemoryAvg), ToDecimal(p.TemperatureAvg))).ToList();

        static List<double> Values(IEnumerable<double?> v) => v.Where(x => x is not null).Select(x => x!.Value).ToList();
        var cpu = Values(series.Points.Select(p => p.CpuAvg));
        var mem = Values(series.Points.Select(p => p.MemoryAvg));
        var temp = Values(series.Points.Select(p => p.TemperatureAvg));
        stats = new Stats(
            cpu.Count > 0 ? cpu.Average() : null,
            series.Points.Max(p => p.CpuMax),
            Percentile95(cpu),
            mem.Count > 0 ? mem.Average() : null,
            temp.Count > 0 ? temp.Average() : null,
            temp.Count > 0 ? temp.Max() : null);
    }

    private string? Info() => series is null ? null : $"risoluzione {ResolutionText(series.BucketSeconds, series.Source)}";

    private static string Pct(double? v) => v is { } x ? x.ToString("0", CultureInfo.CurrentCulture) + " %" : "-";

    private static string Celsius(double? v) => v is { } x ? x.ToString("0", CultureInfo.CurrentCulture) + " °C" : "-";

    private static ApexChartOptions<ChartPoint> BuildOptions()
    {
        const string pct = "function (v) { return v === null || v === undefined ? '' : v.toFixed(0) + ' %'; }";
        const string celsius = "function (v) { return v === null || v === undefined ? '' : v.toFixed(0) + ' °C'; }";
        return new()
        {
            Chart = new Chart { Animations = new Animations { Enabled = false }, Toolbar = new Toolbar { Show = false }, Zoom = new Zoom { Enabled = false } },
            Xaxis = new XAxis { Type = XAxisType.Datetime, Labels = new XAxisLabels { DatetimeUTC = false } },
            // Un asse per serie: picco e memoria condividono la scala 0–100 della CPU (nascosti); la temperatura a destra
            Yaxis =
            [
                new YAxis { SeriesName = "CPU media", Min = 0, Max = 100, TickAmount = 4, Labels = new YAxisLabels { Formatter = pct } },
                new YAxis { SeriesName = "CPU media", Show = false, Min = 0, Max = 100, Labels = new YAxisLabels { Formatter = pct } },
                new YAxis { SeriesName = "CPU media", Show = false, Min = 0, Max = 100, Labels = new YAxisLabels { Formatter = pct } },
                new YAxis { SeriesName = "Temperatura", Opposite = true, Labels = new YAxisLabels { Formatter = celsius } }
            ],
            Tooltip = new Tooltip { Shared = true, X = new TooltipX { Format = "dd/MM HH:mm" } },
            Stroke = new Stroke { Width = 2, Curve = Curve.Straight },
            DataLabels = new DataLabels { Enabled = false },
            Fill = new Fill { Opacity = 0.3 },
            Legend = new Legend { Position = LegendPosition.Top },
            Colors = ["#2e7dd7", "#9bbbe6", "#7b2cbf", "#e07a00"]
        };
    }
}
