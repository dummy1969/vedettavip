// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using ApexCharts;
using Microsoft.AspNetCore.Components;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Web.Client.Components;

/// <summary>
/// Grafico del traffico storico di un'interfaccia (GET /api/metrics/interface): due aree tx/rx orientate come il
/// link sulla mappa, statistiche del periodo (media, picco, 95° percentile, picco % sulla velocità rilevata).
/// </summary>
public partial class InterfaceTrafficChart
{
    [Parameter, EditorRequired] public Guid DeviceId { get; set; }
    [Parameter, EditorRequired] public int IfIndex { get; set; }
    /// <summary>True se il tx del link è l'In dell'interfaccia (interfaccia sul device del nodo di arrivo).</summary>
    [Parameter] public bool TxIsIn { get; set; }
    [Parameter] public string TxLabel { get; set; } = "tx";
    [Parameter] public string RxLabel { get; set; } = "rx";

    /// <summary>X in millisecondi epoch (UTC): con DatetimeUTC = false ApexCharts mostra l'ora locale del browser.</summary>
    private sealed record ChartPoint(long X, decimal? Tx, decimal? Rx);

    private readonly record struct Stats(double? Avg, double? Peak, double? P95);

    private readonly ApexChartOptions<ChartPoint> options = BuildOptions();
    private InterfaceSeriesDto? series;
    private List<ChartPoint>? points;
    private Stats txStats, rxStats;

    protected override object LoadKey => (DeviceId, IfIndex, TxIsIn);

    private bool HasData => points?.Any(p => p.Tx is not null || p.Rx is not null) == true;

    protected override async Task LoadCoreAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        series = await Api.GetInterfaceTrafficAsync(DeviceId, IfIndex, from, to, ct);

        points = series.Points.Select(p => new ChartPoint(
            p.Time.ToUnixTimeMilliseconds(),
            ToDecimal(TxIsIn ? p.InAvg : p.OutAvg),
            ToDecimal(TxIsIn ? p.OutAvg : p.InAvg))).ToList();
        txStats = ComputeStats(series.Points, TxIsIn ? p => (p.InAvg, p.InMax) : p => (p.OutAvg, p.OutMax));
        rxStats = ComputeStats(series.Points, TxIsIn ? p => (p.OutAvg, p.OutMax) : p => (p.InAvg, p.InMax));
    }

    private string? Info() => series is null ? null
        : $"{series.Name ?? $"IfIndex {series.IfIndex}"}{(series.SpeedBps is { } sp ? $" · {NetworkMap.Bps(sp)}bps" : "")}" +
          $" · risoluzione {ResolutionText(series.BucketSeconds, series.Source)}";

    /// <summary>Media e 95° percentile sulle medie dei bucket; picco sul massimo dei campioni.</summary>
    private static Stats ComputeStats(IEnumerable<TrafficPointDto> pts, Func<TrafficPointDto, (double? Avg, double? Max)> pick)
    {
        var values = pts.Select(pick).ToList();
        var avgs = values.Where(v => v.Avg is not null).Select(v => v.Avg!.Value).ToList();
        return avgs.Count == 0 ? default : new Stats(avgs.Average(), values.Max(v => v.Max), Percentile95(avgs));
    }

    private static string Bps(double? v) => v is { } x ? NetworkMap.Bps((long)x) + "bps" : "-";

    private static ApexChartOptions<ChartPoint> BuildOptions() => new()
    {
        Chart = new Chart { Animations = new Animations { Enabled = false }, Toolbar = new Toolbar { Show = false }, Zoom = new Zoom { Enabled = false } },
        Xaxis = new XAxis { Type = XAxisType.Datetime, Labels = new XAxisLabels { DatetimeUTC = false } },
        Yaxis =
        [
            new YAxis
            {
                Min = 0,
                Labels = new YAxisLabels
                {
                    // bps leggibili: 1.2 Mbps, 850 kbps
                    Formatter = """
                        function (v) {
                            if (v === null || v === undefined) return '';
                            const u = ['bps', 'kbps', 'Mbps', 'Gbps', 'Tbps'];
                            let i = 0;
                            while (v >= 1000 && i < u.length - 1) { v /= 1000; i++; }
                            return (i > 0 && v < 10 ? v.toFixed(1) : v.toFixed(0)) + ' ' + u[i];
                        }
                        """
                }
            }
        ],
        Tooltip = new Tooltip { Shared = true, X = new TooltipX { Format = "dd/MM HH:mm" } },
        Stroke = new Stroke { Width = 2, Curve = Curve.Straight },
        DataLabels = new DataLabels { Enabled = false },
        Fill = new Fill { Opacity = 0.25 },
        Legend = new Legend { Position = LegendPosition.Top },
        Colors = ["#2e7dd7", "#2e9e44"]
    };
}
