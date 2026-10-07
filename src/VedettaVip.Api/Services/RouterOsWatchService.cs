// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.EntityFrameworkCore;
using VedettaVip.Api.Data;
using VedettaVip.Api.Data.Entities;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Services;

/// <summary>
/// A ogni report RouterOS confronta interfacce e peer sorvegliati con la lettura (<see cref="RouterOsWatchRules"/>) e
/// apre/chiude gli avvisi come eventi ThresholdRaised/ThresholdCleared (AlertKey "if:…" / "wg:…"): così ritardo di invio,
/// flap, manutenzione, promemoria e presa in carico sono quelli delle soglie. Letture fallite: nessuna valutazione.
/// </summary>
public sealed class RouterOsWatchService(VedettaVipDbContext db, TimeProvider time, ILogger<RouterOsWatchService> logger)
{
    /// <summary>Valuta le letture e restituisce gli elementi sorvegliati giù per device (anche quelli senza cambi).</summary>
    public async Task<Dictionary<Guid, int>> ProcessAsync(IReadOnlyList<RouterOsSampleDto> samples, CancellationToken ct)
    {
        var bySample = samples.Where(s => s.Ok).GroupBy(s => s.DeviceId).ToDictionary(g => g.Key, g => g.MaxBy(s => s.Time)!);
        if (bySample.Count == 0)
            return [];

        var ids = bySample.Keys.ToList();
        var watches = await db.RouterOsWatches.Where(w => ids.Contains(w.DeviceId)).ToListAsync(ct);
        if (watches.Count == 0)
            return [];

        var devices = await db.Devices.AsNoTracking().Where(d => ids.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, d => new { d.Name, d.Address }, ct);
        var now = time.GetUtcNow();
        var cleared = new List<(Guid DeviceId, string AlertKey)>();

        foreach (var w in watches)
        {
            var sample = bySample[w.DeviceId];
            if (RouterOsWatchRules.Observe(w.Kind, w.Key, sample) is not var (up, detail))
                continue;

            var (state, downReads, transition) = RouterOsWatchRules.Next(w.State, w.DownReads, up);
            if (state != w.State)
                w.Since = sample.Time;
            (w.State, w.DownReads, w.Detail) = (state, downReads, detail);

            var device = devices.GetValueOrDefault(w.DeviceId);
            var who = device is null ? "?" : $"{device.Name} ({device.Address})";
            var what = w.Kind == RouterOsWatchKind.Interface ? $"interfaccia {w.Label}" : $"tunnel WireGuard {w.Label}";
            var alertKey = RouterOsWatchRules.AlertKey(w.Kind, w.Key);
            switch (transition)
            {
                case WatchTransition.Raise:
                    db.Events.Add(new Event
                    {
                        DeviceId = w.DeviceId, Time = sample.Time, Severity = EventSeverity.Error, Type = EventTypes.ThresholdRaised,
                        AlertKey = alertKey, Message = $"{who}: {what} giù ({detail})", NotifyState = NotifyState.Pending, NotifyAfter = now
                    });
                    break;
                case WatchTransition.Clear:
                    db.Events.Add(new Event
                    {
                        DeviceId = w.DeviceId, Time = sample.Time, Severity = EventSeverity.Info, Type = EventTypes.ThresholdCleared,
                        AlertKey = alertKey, Acknowledged = true, Message = $"{who}: {what} di nuovo {(w.Kind == RouterOsWatchKind.Interface ? "attiva" : "attivo")} ({detail})",
                        NotifyState = NotifyState.Pending, NotifyAfter = now
                    });
                    cleared.Add((w.DeviceId, alertKey));
                    break;
            }
            if (transition != WatchTransition.None)
                logger.LogInformation("{Who}: {What} {State} ({Detail})", who, what, state, detail);
        }

        await db.SaveChangesAsync(ct);
        foreach (var (deviceId, alertKey) in cleared)
            await CloseOpenAlertAsync(deviceId, alertKey, ct);

        return watches.Where(w => w.State == RouterOsWatchState.Down).GroupBy(w => w.DeviceId).ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>Prende in carico e chiude l'avviso aperto (ripristino, o sorveglianza tolta).</summary>
    public Task CloseOpenAlertAsync(Guid deviceId, string alertKey, CancellationToken ct) =>
        db.Events
            .Where(e => e.DeviceId == deviceId && e.Type == EventTypes.ThresholdRaised && e.AlertKey == alertKey && !e.Acknowledged)
            .ExecuteUpdateAsync(u => u.SetProperty(e => e.Acknowledged, true).SetProperty(e => e.ResolvedAt, time.GetUtcNow()), ct);
}
