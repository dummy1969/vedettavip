// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Shared.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace VedettaVip.Api.Services;

/// <summary>Nomi delle metriche nella hypertable "Metrics".</summary>
public static class MetricNames
{
    /// <summary>Traffico in ingresso dell'interfaccia, bps.</summary>
    public const string InterfaceInBps = "if.in_bps";
    /// <summary>Traffico in uscita dell'interfaccia, bps.</summary>
    public const string InterfaceOutBps = "if.out_bps";
    /// <summary>RTT del ping riuscito, ms (i ping persi non hanno RTT).</summary>
    public const string IcmpRttMs = "icmp.rtt_ms";
    /// <summary>1 = ping perso, 0 = riuscito: la media è la frazione di perdita (anche negli aggregati).</summary>
    public const string IcmpLoss = "icmp.loss";
    /// <summary>RouterOS: carico CPU, %.</summary>
    public const string RouterOsCpuPct = "ros.cpu_pct";
    /// <summary>RouterOS: memoria usata, % del totale.</summary>
    public const string RouterOsMemoryPct = "ros.mem_pct";
    /// <summary>RouterOS: temperatura (CPU, o scheda se manca), °C.</summary>
    public const string RouterOsTemperatureC = "ros.temp_c";
    /// <summary>RouterOS: tensione di alimentazione, V.</summary>
    public const string RouterOsVoltageV = "ros.voltage_v";
}

/// <summary>
/// Scrive i campioni nella hypertable "Metrics" con COPY binario (un round-trip per report, migliaia di righe/s).
/// "Metrics" non è nel modello EF: vedi la migration AddMetrics.
/// </summary>
public sealed class MetricWriter(NpgsqlDataSource dataSource, ILogger<MetricWriter> logger)
{
    private const string Copy = """
        COPY "Metrics" ("Time", "DeviceId", "IfIndex", "Name", "Value") FROM STDIN (FORMAT BINARY)
        """;

    /// <summary>
    /// Traffico: due serie per interfaccia (in/out). Ping: perdita sempre, RTT solo se riuscito.
    /// Restituisce le righe scritte.
    /// </summary>
    public async Task<int> WriteAsync(IReadOnlyList<InterfaceTrafficDto> traffic, IReadOnlyList<DevicePingDto> pings, CancellationToken ct)
    {
        if (traffic.Count == 0 && pings.Count == 0)
            return 0;

        var rows = 0;
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var writer = await connection.BeginBinaryImportAsync(Copy, ct);

        foreach (var s in traffic)
        {
            await WriteRowAsync(writer, s.Time, s.DeviceId, s.IfIndex, MetricNames.InterfaceInBps, s.InBps, ct);
            await WriteRowAsync(writer, s.Time, s.DeviceId, s.IfIndex, MetricNames.InterfaceOutBps, s.OutBps, ct);
            rows += 2;
        }

        foreach (var p in pings)
        {
            await WriteRowAsync(writer, p.Time, p.DeviceId, null, MetricNames.IcmpLoss, p.Success ? 0 : 1, ct);
            rows++;
            if (p is { Success: true, RttMs: { } rtt })
            {
                await WriteRowAsync(writer, p.Time, p.DeviceId, null, MetricNames.IcmpRttMs, rtt, ct);
                rows++;
            }
        }

        await writer.CompleteAsync(ct);
        logger.LogDebug("Metriche scritte: {Rows} righe", rows);
        return rows;
    }

    /// <summary>Letture RouterOS riuscite: una riga per valore presente (i sensori assenti non producono righe).</summary>
    public async Task<int> WriteRouterOsAsync(IReadOnlyList<RouterOsSampleDto> samples, CancellationToken ct)
    {
        var rows = samples.Where(s => s.Ok)
            .SelectMany(s => new (string Name, double? Value)[]
                {
                    (MetricNames.RouterOsCpuPct, s.CpuLoadPct), (MetricNames.RouterOsMemoryPct, s.MemoryUsedPct),
                    (MetricNames.RouterOsTemperatureC, s.TemperatureC), (MetricNames.RouterOsVoltageV, s.VoltageV)
                }
                .Where(v => v.Value is not null)
                .Select(v => (s.Time, s.DeviceId, v.Name, Value: v.Value!.Value)))
            .ToList();
        if (rows.Count == 0)
            return 0;

        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var writer = await connection.BeginBinaryImportAsync(Copy, ct);
        foreach (var r in rows)
            await WriteRowAsync(writer, r.Time, r.DeviceId, null, r.Name, r.Value, ct);
        await writer.CompleteAsync(ct);
        return rows.Count;
    }

    private static async Task WriteRowAsync(
        NpgsqlBinaryImporter writer, DateTimeOffset time, Guid deviceId, int? ifIndex, string name, double value, CancellationToken ct)
    {
        await writer.StartRowAsync(ct);
        await writer.WriteAsync(time.ToUniversalTime(), NpgsqlDbType.TimestampTz, ct);
        await writer.WriteAsync(deviceId, NpgsqlDbType.Uuid, ct);
        if (ifIndex is { } i)
            await writer.WriteAsync(i, NpgsqlDbType.Integer, ct);
        else
            await writer.WriteNullAsync(ct);
        await writer.WriteAsync(name, NpgsqlDbType.Varchar, ct);
        await writer.WriteAsync(value, NpgsqlDbType.Double, ct);
    }
}
