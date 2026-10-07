// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.EntityFrameworkCore;
using VedettaVip.Api.Data;
using VedettaVip.Shared.Contracts;
using Npgsql;

namespace VedettaVip.Api.Services;

/// <summary>
/// Sceglie sorgente e ampiezza del bucket per un intervallo: circa <see cref="TargetPoints"/> punti, con i
/// campioni grezzi solo finché sono dentro la retention (7 giorni) e l'aggregato a 5 minuti entro i 90 giorni.
/// </summary>
public static class MetricResolution
{
    public const int TargetPoints = 500;

    public static readonly TimeSpan RawRetention = TimeSpan.FromDays(7);
    public static readonly TimeSpan FiveMinuteRetention = TimeSpan.FromDays(90);
    public static readonly TimeSpan RawInterval = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan[] Buckets =
    [
        TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30), TimeSpan.FromHours(1),
        TimeSpan.FromHours(2), TimeSpan.FromHours(3), TimeSpan.FromHours(6), TimeSpan.FromHours(12),
        TimeSpan.FromDays(1), TimeSpan.FromDays(2), TimeSpan.FromDays(7)
    ];

    public static (MetricSource Source, TimeSpan Bucket) Choose(DateTimeOffset from, DateTimeOffset to, DateTimeOffset now)
    {
        var wanted = (to - from) / TargetPoints;
        var bucket = Buckets.FirstOrDefault(b => b >= wanted, Buckets[^1]);

        if (bucket < TimeSpan.FromMinutes(5) && from >= now - RawRetention)
            return (MetricSource.Raw, Max(bucket, RawInterval));
        if (bucket < TimeSpan.FromHours(1) && from >= now - FiveMinuteRetention)
            return (MetricSource.FiveMinutes, Max(bucket, TimeSpan.FromMinutes(5)));
        return (MetricSource.OneHour, Max(bucket, TimeSpan.FromHours(1)));
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}

/// <summary>
/// Letture delle serie storiche da "Metrics" e dai continuous aggregate (SQL, fuori dal modello EF).
/// Ogni serie è descritta da colonne (alias, metrica, media o massimo); l'SQL per grezzi e aggregati è generato
/// dalla stessa descrizione: sui grezzi avg/max dei valori, sugli aggregati media pesata sui campioni e max dei Max.
/// </summary>
public sealed class MetricQuery(VedettaVipDbContext db, TimeProvider time)
{
    private enum Agg { Avg, Max }

    private sealed record Column(string Alias, string Metric, Agg Agg, double Scale = 1);

    private static readonly Column[] TrafficColumns =
    [
        new("InAvg", MetricNames.InterfaceInBps, Agg.Avg), new("InMax", MetricNames.InterfaceInBps, Agg.Max),
        new("OutAvg", MetricNames.InterfaceOutBps, Agg.Avg), new("OutMax", MetricNames.InterfaceOutBps, Agg.Max)
    ];

    private static readonly Column[] LatencyColumns =
    [
        new("RttAvg", MetricNames.IcmpRttMs, Agg.Avg), new("RttMax", MetricNames.IcmpRttMs, Agg.Max),
        new("LossPct", MetricNames.IcmpLoss, Agg.Avg, Scale: 100)
    ];

    public async Task<InterfaceSeriesDto> InterfaceTrafficAsync(
        Guid deviceId, int ifIndex, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var (source, bucket) = MetricResolution.Choose(from, to, time.GetUtcNow());
        var rows = await QueryAsync<TrafficRow>(source, bucket, TrafficColumns, deviceId, ifIndex, from, to, ct);

        var info = await db.DeviceInterfaces.AsNoTracking()
            .Where(i => i.DeviceId == deviceId && i.IfIndex == ifIndex)
            .Select(i => new { i.Name, i.SpeedBps })
            .FirstOrDefaultAsync(ct);

        return new InterfaceSeriesDto(deviceId, ifIndex, info?.Name, info?.SpeedBps, from, to, (int)bucket.TotalSeconds, source,
            rows.Select(r => new TrafficPointDto(r.Time, r.InAvg, r.InMax, r.OutAvg, r.OutMax)).ToList());
    }

    private static readonly Column[] RouterOsColumns =
    [
        new("CpuAvg", MetricNames.RouterOsCpuPct, Agg.Avg), new("CpuMax", MetricNames.RouterOsCpuPct, Agg.Max),
        new("MemoryAvg", MetricNames.RouterOsMemoryPct, Agg.Avg), new("TemperatureAvg", MetricNames.RouterOsTemperatureC, Agg.Avg)
    ];

    /// <summary>Il Worker legge RouterOS ogni 60 s: sui grezzi un bucket più piccolo avrebbe un punto vuoto su due.</summary>
    private static readonly TimeSpan RouterOsRawInterval = TimeSpan.FromSeconds(60);

    public async Task<DeviceRouterOsSeriesDto> DeviceRouterOsAsync(Guid deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var (source, bucket) = MetricResolution.Choose(from, to, time.GetUtcNow());
        if (bucket < RouterOsRawInterval)
            bucket = RouterOsRawInterval;
        var rows = await QueryAsync<RouterOsRow>(source, bucket, RouterOsColumns, deviceId, null, from, to, ct);

        return new DeviceRouterOsSeriesDto(deviceId, from, to, (int)bucket.TotalSeconds, source,
            rows.Select(r => new RouterOsPointDto(r.Time, r.CpuAvg, r.CpuMax, r.MemoryAvg, r.TemperatureAvg)).ToList());
    }

    public async Task<DeviceLatencySeriesDto> DeviceLatencyAsync(Guid deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var (source, bucket) = MetricResolution.Choose(from, to, time.GetUtcNow());
        var rows = await QueryAsync<LatencyRow>(source, bucket, LatencyColumns, deviceId, null, from, to, ct);

        return new DeviceLatencySeriesDto(deviceId, from, to, (int)bucket.TotalSeconds, source,
            rows.Select(r => new LatencyPointDto(r.Time, r.RttAvg, r.RttMax, r.LossPct)).ToList());
    }

    /// <summary>Interfacce del device con storico del traffico negli ultimi 90 giorni (dall'aggregato a 5 minuti).</summary>
    public async Task<List<InterfaceWithHistoryDto>> InterfacesWithHistoryAsync(Guid deviceId, CancellationToken ct)
    {
        var ifIndexes = await db.Database.SqlQueryRaw<int>($"""
                SELECT DISTINCT "IfIndex" AS "Value" FROM "Metrics5m"
                WHERE "DeviceId" = @device AND "IfIndex" IS NOT NULL AND "Name" = '{MetricNames.InterfaceInBps}'
                """, new NpgsqlParameter("device", deviceId))
            .ToListAsync(ct);

        var names = await db.DeviceInterfaces.AsNoTracking()
            .Where(i => i.DeviceId == deviceId && ifIndexes.Contains(i.IfIndex))
            .ToDictionaryAsync(i => i.IfIndex, ct);

        return ifIndexes.Order()
            .Select(i => names.TryGetValue(i, out var n)
                ? new InterfaceWithHistoryDto(i, n.Name, n.Alias, n.SpeedBps)
                : new InterfaceWithHistoryDto(i, null, null, null))
            .ToList();
    }

    private async Task<List<T>> QueryAsync<T>(
        MetricSource source, TimeSpan bucket, Column[] columns, Guid deviceId, int? ifIndex,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct) where T : class
    {
        var raw = source == MetricSource.Raw;
        var table = source switch
        {
            MetricSource.Raw => "\"Metrics\"",
            MetricSource.FiveMinutes => "\"Metrics5m\"",
            _ => "\"Metrics1h\""
        };
        var timeColumn = raw ? "\"Time\"" : "\"Bucket\"";
        var select = string.Join(",\n       ", columns.Select(c => $"{Expression(c, raw)} AS \"{c.Alias}\""));
        var names = string.Join(", ", columns.Select(c => $"'{c.Metric}'").Distinct());

        // Nomi di tabella e metriche da costanti interne; i valori variabili sono tutti parametri
        var sql = $"""
            SELECT time_bucket_gapfill(@bucket, {timeColumn}, @from, @to) AS "Time",
                   {select}
            FROM {table}
            WHERE "DeviceId" = @device AND {(ifIndex is null ? "\"IfIndex\" IS NULL" : "\"IfIndex\" = @ifIndex")}
              AND "Name" IN ({names})
              AND {timeColumn} >= @from AND {timeColumn} < @to
            GROUP BY 1
            ORDER BY 1
            """;

        // Npgsql vuole timestamptz in UTC (offset 0)
        var parameters = new List<NpgsqlParameter>
        {
            new("bucket", bucket),
            new("from", from.ToUniversalTime()),
            new("to", to.ToUniversalTime()),
            new("device", deviceId)
        };
        if (ifIndex is { } i)
            parameters.Add(new NpgsqlParameter("ifIndex", i));

        return await db.Database.SqlQueryRaw<T>(sql, parameters.ToArray<object>()).ToListAsync(ct);
    }

    private static string Expression(Column c, bool raw)
    {
        var filter = $"FILTER (WHERE \"Name\" = '{c.Metric}')";
        var expression = (c.Agg, raw) switch
        {
            (Agg.Avg, true) => $"avg(\"Value\") {filter}",
            (Agg.Max, true) => $"max(\"Value\") {filter}",
            // Media pesata sul numero di campioni: la media delle medie sarebbe sbagliata con bucket incompleti
            (Agg.Avg, false) => $"sum(\"Avg\" * \"Samples\") {filter} / NULLIF(sum(\"Samples\") {filter}, 0)",
            _ => $"max(\"Max\") {filter}"
        };
        return c.Scale == 1 ? expression : $"({expression}) * {c.Scale.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private sealed class TrafficRow
    {
        public DateTimeOffset Time { get; init; }
        public double? InAvg { get; init; }
        public double? InMax { get; init; }
        public double? OutAvg { get; init; }
        public double? OutMax { get; init; }
    }

    private sealed class RouterOsRow
    {
        public DateTimeOffset Time { get; init; }
        public double? CpuAvg { get; init; }
        public double? CpuMax { get; init; }
        public double? MemoryAvg { get; init; }
        public double? TemperatureAvg { get; init; }
    }

    private sealed class LatencyRow
    {
        public DateTimeOffset Time { get; init; }
        public double? RttAvg { get; init; }
        public double? RttMax { get; init; }
        public double? LossPct { get; init; }
    }
}
