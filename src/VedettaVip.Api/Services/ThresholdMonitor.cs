// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.EntityFrameworkCore;
using VedettaVip.Api.Data;
using VedettaVip.Api.Data.Entities;
using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;
using Npgsql;

namespace VedettaVip.Api.Services;

/// <summary>
/// Ogni minuto valuta le soglie sulla media della finestra (campioni grezzi in "Metrics"):
/// <list type="bullet">
/// <item>latenza (RTT medio dei ping riusciti) e perdita (% di ping persi) per device Up o Partial: un device
/// Down ha già il suo avviso e i suoi ping persi non sono "perdita";</item>
/// <item>utilizzo dei link con sorgente di traffico: max(tx, rx) / velocità del link;</item>
/// <item>CPU e temperatura dei router con API RouterOS (letture ogni 60 s), stessi criteri dei device.</item>
/// </list>
/// Apertura sopra soglia, chiusura sotto l'80% (<see cref="ThresholdRules"/>). Ogni transizione è un evento
/// (ThresholdRaised / ThresholdCleared con AlertKey) che passa dal motore delle notifiche; la chiusura prende in carico
/// automaticamente l'apertura. Lo stato degli avvisi aperti è in OpenThresholdAlerts.
/// </summary>
public sealed class ThresholdMonitor(IServiceScopeFactory scopes, TimeProvider time, ILogger<ThresholdMonitor> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private sealed record DeviceRow(Guid Id, string Name, string Address, bool Enabled, NodeState State, int? RttMs, double? LossPct,
        bool RouterOs = false, int? CpuPct = null, int? TemperatureC = null);
    private sealed record LinkRow(Guid Id, Guid DeviceId, int IfIndex, long SpeedBps, int? UtilizationPct, string FromName, string ToName);
    private sealed class DeviceStats { public Guid DeviceId { get; init; } public double? RttAvg { get; init; } public double? LossAvg { get; init; } public int Pings { get; init; } }
    private sealed class RouterOsStats { public Guid DeviceId { get; init; } public double? CpuAvg { get; init; } public double? TempAvg { get; init; } public int Samples { get; init; } }
    private sealed class InterfaceStats { public Guid DeviceId { get; init; } public int IfIndex { get; init; } public double? InAvg { get; init; } public double? OutAvg { get; init; } public int Samples { get; init; } }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await EvaluateAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Valutazione delle soglie fallita");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task EvaluateAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VedettaVipDbContext>();
        var now = time.GetUtcNow();

        var settings = await db.MonitoringSettings.AsNoTracking().FirstAsync(ct);
        var window = TimeSpan.FromMinutes(settings.ThresholdWindowMinutes);
        var from = now - window;
        var minSamples = ThresholdRules.MinSamples(settings.ThresholdWindowMinutes);

        var open = await db.OpenThresholdAlerts.ToDictionaryAsync(a => (a.Kind, a.DeviceId, a.LinkId), ct);
        var devices = await db.Devices.AsNoTracking()
            .Select(d => new DeviceRow(d.Id, d.Name, d.Address, d.Enabled, d.Status != null ? d.Status.State : NodeState.Unknown,
                d.RttThresholdMs, d.LossThresholdPct, d.RouterOsApiEnabled, d.CpuThresholdPct, d.TemperatureThresholdC))
            .ToDictionaryAsync(d => d.Id, ct);

        var deviceStats = (await db.Database.SqlQueryRaw<DeviceStats>($"""
                SELECT "DeviceId",
                       avg("Value") FILTER (WHERE "Name" = '{MetricNames.IcmpRttMs}') AS "RttAvg",
                       avg("Value") FILTER (WHERE "Name" = '{MetricNames.IcmpLoss}') * 100 AS "LossAvg",
                       (count(*) FILTER (WHERE "Name" = '{MetricNames.IcmpLoss}'))::int AS "Pings"
                FROM "Metrics"
                WHERE "Time" >= @from AND "IfIndex" IS NULL AND "Name" IN ('{MetricNames.IcmpRttMs}', '{MetricNames.IcmpLoss}')
                GROUP BY "DeviceId"
                """, new NpgsqlParameter("from", from.ToUniversalTime())).ToListAsync(ct))
            .ToDictionary(s => s.DeviceId);

        var changes = 0;
        var cleared = new List<(Guid DeviceId, string AlertKey)>();
        foreach (var device in devices.Values)
        {
            // Solo device monitorati e raggiungibili: Down/Unknown non hanno latenza significativa
            var evaluable = device.Enabled && device.State is NodeState.Up or NodeState.Partial
                            && deviceStats.TryGetValue(device.Id, out var stats) && stats.Pings >= minSamples;
            var s = evaluable ? deviceStats[device.Id] : null;

            changes += Apply(db, open, cleared, ThresholdKind.Latency, device, s?.RttAvg, ThresholdRules.Effective(device.RttMs, settings.RttThresholdMs),
                evaluable, now, v => $"latenza media {v:0.#} ms", "ms", window, null);
            changes += Apply(db, open, cleared, ThresholdKind.Loss, device, s?.LossAvg, ThresholdRules.Effective(device.LossPct, settings.LossThresholdPct),
                evaluable, now, v => $"perdita {v:0.#} %", "%", window, null);
        }

        // RouterOS: CPU e temperatura (una lettura al minuto: basta metà della finestra)
        var rosStats = (await db.Database.SqlQueryRaw<RouterOsStats>($"""
                SELECT "DeviceId",
                       avg("Value") FILTER (WHERE "Name" = '{MetricNames.RouterOsCpuPct}') AS "CpuAvg",
                       avg("Value") FILTER (WHERE "Name" = '{MetricNames.RouterOsTemperatureC}') AS "TempAvg",
                       (count(*) FILTER (WHERE "Name" = '{MetricNames.RouterOsCpuPct}'))::int AS "Samples"
                FROM "Metrics"
                WHERE "Time" >= @from AND "IfIndex" IS NULL AND "Name" IN ('{MetricNames.RouterOsCpuPct}', '{MetricNames.RouterOsTemperatureC}')
                GROUP BY "DeviceId"
                """, new NpgsqlParameter("from", from.ToUniversalTime())).ToListAsync(ct))
            .ToDictionary(s => s.DeviceId);
        var rosMinSamples = Math.Max(2, settings.ThresholdWindowMinutes / 2);
        foreach (var device in devices.Values.Where(d => d.RouterOs || open.ContainsKey((nameof(ThresholdKind.RouterOsCpu), d.Id, Guid.Empty))
                                                         || open.ContainsKey((nameof(ThresholdKind.RouterOsTemperature), d.Id, Guid.Empty))))
        {
            var evaluable = device.RouterOs && device.Enabled && device.State is NodeState.Up or NodeState.Partial
                            && rosStats.TryGetValue(device.Id, out var rs) && rs.Samples >= rosMinSamples;
            var r = evaluable ? rosStats[device.Id] : null;
            // API RouterOS disattivata sul device: soglie considerate disattivate, gli avvisi aperti si chiudono
            changes += Apply(db, open, cleared, ThresholdKind.RouterOsCpu, device, r?.CpuAvg,
                device.RouterOs ? ThresholdRules.Effective(device.CpuPct, settings.RouterOsCpuThresholdPct) : null,
                evaluable, now, v => $"CPU media {v:0} %", "%", window, null);
            changes += Apply(db, open, cleared, ThresholdKind.RouterOsTemperature, device, r?.TempAvg,
                device.RouterOs ? ThresholdRules.Effective(device.TemperatureC, settings.RouterOsTemperatureThresholdC) : null,
                evaluable, now, v => $"temperatura media {v:0} °C", "°C", window, null);
        }

        // Link con sorgente di traffico
        var links = await db.MapLinks.AsNoTracking()
            .Where(l => l.DeviceId != null && l.IfIndex != null)
            .Select(l => new LinkRow(l.Id, l.DeviceId!.Value, l.IfIndex!.Value, l.SpeedBps, l.UtilizationThresholdPct,
                l.FromNode!.Device != null ? l.FromNode.Device.Name : l.FromNode.Submap != null ? l.FromNode.Submap.Name : l.FromNode.LabelTemplate,
                l.ToNode!.Device != null ? l.ToNode.Device.Name : l.ToNode.Submap != null ? l.ToNode.Submap.Name : l.ToNode.LabelTemplate))
            .ToListAsync(ct);
        if (links.Count > 0)
        {
            var interfaceStats = (await db.Database.SqlQueryRaw<InterfaceStats>($"""
                    SELECT "DeviceId", "IfIndex",
                           avg("Value") FILTER (WHERE "Name" = '{MetricNames.InterfaceInBps}') AS "InAvg",
                           avg("Value") FILTER (WHERE "Name" = '{MetricNames.InterfaceOutBps}') AS "OutAvg",
                           (count(*) FILTER (WHERE "Name" = '{MetricNames.InterfaceInBps}'))::int AS "Samples"
                    FROM "Metrics"
                    WHERE "Time" >= @from AND "IfIndex" IS NOT NULL AND "Name" IN ('{MetricNames.InterfaceInBps}', '{MetricNames.InterfaceOutBps}')
                    GROUP BY "DeviceId", "IfIndex"
                    """, new NpgsqlParameter("from", from.ToUniversalTime())).ToListAsync(ct))
                .ToDictionary(s => (s.DeviceId, s.IfIndex));

            foreach (var link in links)
            {
                if (!devices.TryGetValue(link.DeviceId, out var device))
                    continue;
                var evaluable = interfaceStats.TryGetValue((link.DeviceId, link.IfIndex), out var st) && st.Samples >= minSamples && link.SpeedBps > 0;
                double? utilization = evaluable ? Math.Max(st!.InAvg ?? 0, st.OutAvg ?? 0) / link.SpeedBps * 100 : null;
                changes += Apply(db, open, cleared, ThresholdKind.LinkUtilization, device, utilization,
                    ThresholdRules.Effective(link.UtilizationPct, settings.LinkUtilizationThresholdPct), evaluable, now,
                    v => $"link {link.FromName} – {link.ToName} al {v:0} %", "%", window, link.Id);
            }

            // Avvisi di link eliminati o non più misurati: si chiudono senza evento
            var linkIds = links.Select(l => l.Id).ToHashSet();
            foreach (var stale in open.Values.Where(a => a.Kind == nameof(ThresholdKind.LinkUtilization) && !linkIds.Contains(a.LinkId)).ToList())
                db.OpenThresholdAlerts.Remove(stale);
        }

        await db.SaveChangesAsync(ct);

        // La chiusura prende in carico l'apertura (come il ripristino di un Down)
        foreach (var (deviceId, alertKey) in cleared)
            await db.Events
                .Where(e => e.DeviceId == deviceId && e.Type == EventTypes.ThresholdRaised && e.AlertKey == alertKey && !e.Acknowledged)
                .ExecuteUpdateAsync(u => u.SetProperty(e => e.Acknowledged, true).SetProperty(e => e.ResolvedAt, now), ct);

        if (changes > 0)
            logger.LogInformation("Soglie: {Changes} avvisi aperti o chiusi", changes);
    }

    /// <summary>Applica la transizione di un avviso; restituisce 1 se si è aperto o chiuso.</summary>
    private static int Apply(
        VedettaVipDbContext db, Dictionary<(string, Guid, Guid), OpenThresholdAlert> open, List<(Guid, string)> cleared,
        ThresholdKind kind, DeviceRow device, double? value, double? threshold, bool evaluable, DateTimeOffset now, Func<double, string> describe, string unit, TimeSpan window, Guid? linkId)
    {
        var key = (kind.ToString(), device.Id, linkId ?? Guid.Empty);
        open.TryGetValue(key, out var alert);

        // Senza dati sufficienti non si decide nulla, salvo chiudere un avviso la cui soglia è stata disattivata
        if (!evaluable || value is not { } v)
        {
            if (alert is null || threshold is not null)
                return 0;
            v = 0;
        }

        var alertKey = ThresholdRules.Key(kind, linkId);
        switch (ThresholdRules.Evaluate(alert is not null, v, threshold, ThresholdRules.ClearLevel(kind, threshold)))
        {
            case ThresholdTransition.Raise:
                db.OpenThresholdAlerts.Add(new OpenThresholdAlert
                {
                    Kind = kind.ToString(), DeviceId = device.Id, LinkId = linkId ?? Guid.Empty,
                    OpenedAt = now, Threshold = threshold!.Value, LastValue = v, PeakValue = v
                });
                db.Events.Add(new Event
                {
                    DeviceId = device.Id,
                    Time = now,
                    Severity = EventSeverity.Warning,
                    Type = EventTypes.ThresholdRaised,
                    AlertKey = alertKey,
                    Message = $"{device.Name} ({device.Address}): {describe(v)} negli ultimi {window.TotalMinutes:0} min, soglia {threshold:0.#} {unit}",
                    NotifyState = NotifyState.Pending,
                    NotifyAfter = now
                });
                return 1;

            case ThresholdTransition.Clear:
                db.OpenThresholdAlerts.Remove(alert!);
                db.Events.Add(new Event
                {
                    DeviceId = device.Id,
                    Time = now,
                    Severity = EventSeverity.Info,
                    Type = EventTypes.ThresholdCleared,
                    AlertKey = alertKey,
                    Acknowledged = true,
                    Message = threshold is null
                        ? $"{device.Name} ({device.Address}): soglia disattivata, avviso chiuso"
                        : $"{device.Name} ({device.Address}): {describe(v)}, rientrato sotto la soglia (picco {alert!.PeakValue:0.#} {unit})",
                    NotifyState = NotifyState.Pending,
                    NotifyAfter = now
                });
                cleared.Add((device.Id, alertKey));
                return 1;

            default:
                if (alert is not null)
                {
                    alert.LastValue = v;
                    alert.PeakValue = Math.Max(alert.PeakValue, v);
                }
                return 0;
        }
    }
}
