// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VedettaVip.Api.Data;
using VedettaVip.Api.Data.Entities;
using VedettaVip.Api.Hubs;
using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;

namespace VedettaVip.Api.Services;

/// <summary>
/// Serializza l'elaborazione dei report: un report di cambi e uno snapshot arrivati insieme non devono
/// generare eventi doppi. Valido con una sola istanza dell'API; con più istanze serve un lock su
/// PostgreSQL (pg_advisory_xact_lock).
/// </summary>
public sealed class StatusProcessingLock
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
}

public sealed class DeviceStatusService(
    VedettaVipDbContext db,
    IHubContext<StatusHub> hub,
    StatusProcessingLock processingLock,
    TimeProvider time,
    ILogger<DeviceStatusService> logger)
{
    public async Task<AgentStatusAckDto> ProcessAsync(AgentStatusReportDto report, CancellationToken ct)
    {
        var accepted = 0;
        var unknownDevices = new List<Guid>();
        var changes = new List<DeviceStateChangedDto>();
        // Device tornati Up da un problema: i loro eventi di problema aperti si chiudono da soli
        var recoveries = new List<(Guid DeviceId, DateTimeOffset At)>();

        await processingLock.Semaphore.WaitAsync(ct);
        try
        {
            var now = time.GetUtcNow();
            await TouchAgentAsync(report.AgentId, now, ct);
            DateTimeOffset? notifyAfter = null; // letto solo se c'è almeno un cambio

            var ids = report.Results.Select(r => r.DeviceId).Distinct().ToList();
            var devices = await db.Devices
                .Include(d => d.Status)
                .Where(d => ids.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, ct);

            // In ordine di rilevazione: i cambi bufferizzati dal Worker diventano eventi nell'ordine corretto
            foreach (var result in report.Results.OrderBy(r => r.Time))
            {
                if (!devices.TryGetValue(result.DeviceId, out var device))
                {
                    unknownDevices.Add(result.DeviceId);
                    continue;
                }

                accepted++;
                if (result.State == NodeState.Unknown)
                    continue; // l'agente non deve inviarli; ignorati per non sovrascrivere l'ultimo stato noto

                var observedAt = result.Time.ToUniversalTime();
                var status = device.Status;
                if (status is not null && observedAt < status.ObservedAt)
                    continue; // risultato superato (es. ritrasmesso dal buffer dopo uno più recente)

                var previous = status?.State ?? NodeState.Unknown;
                if (status is null)
                {
                    status = new DeviceStatus { DeviceId = device.Id, AgentId = report.AgentId, Since = observedAt };
                    db.DeviceStatuses.Add(status);
                    device.Status = status;
                }

                status.ObservedAt = observedAt;
                status.LastReportAt = now;
                status.LastRttMs = result.RttMs;
                status.SnmpOk = result.SnmpOk;
                status.AgentId = report.AgentId;

                if (previous == result.State)
                    continue;

                status.State = result.State;
                status.Since = observedAt;

                // L'evento è anche l'outbox delle notifiche: il dispatcher lo valuta dopo il ritardo configurato
                var notifiable = AlertRules.KindOf(EventTypes.StateChange, previous, result.State) is not null;
                if (notifiable && notifyAfter is null)
                    notifyAfter = now + TimeSpan.FromSeconds(await NotifyDelaySecondsAsync(ct));

                var severity = SeverityOf(result.State);
                if (result.State == NodeState.Up && previous is NodeState.Down or NodeState.Partial)
                    recoveries.Add((device.Id, observedAt));

                db.Events.Add(new Event
                {
                    DeviceId = device.Id,
                    Time = observedAt,
                    Severity = severity,
                    // Gli eventi informativi (ripristino, prima rilevazione) non richiedono di essere gestiti
                    Acknowledged = severity == EventSeverity.Info,
                    Type = EventTypes.StateChange,
                    Message = $"{device.Name} ({device.Address}): {previous} → {result.State}",
                    FromState = previous,
                    ToState = result.State,
                    NotifyState = notifiable ? NotifyState.Pending : NotifyState.None,
                    NotifyAfter = notifiable ? notifyAfter : null
                });
                changes.Add(new DeviceStateChangedDto(device.Id, result.State, observedAt));
            }

            // Stessa transazione: un Down e il suo ripristino arrivati nello stesso report si chiudono insieme
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.SaveChangesAsync(ct);
            foreach (var (deviceId, at) in recoveries)
                await ResolveOpenProblemsAsync(e => e.DeviceId == deviceId && e.Type == EventTypes.StateChange
                                                   && (e.ToState == NodeState.Down || e.ToState == NodeState.Partial), at, ct);
            if (agentBackOnlineAt is { } onlineAt)
                await ResolveOpenProblemsAsync(e => e.AgentId == report.AgentId && e.Type == EventTypes.AgentOffline, onlineAt, ct);
            await tx.CommitAsync(ct);
        }
        finally
        {
            processingLock.Semaphore.Release();
        }

        if (unknownDevices.Count > 0)
            logger.LogWarning("Agente {AgentId}: {Count} risultati per dispositivi inesistenti", report.AgentId, unknownDevices.Count);

        // Notifica solo dopo il commit; un errore SignalR non deve far ritrasmettere il report
        foreach (var change in changes)
        {
            logger.LogInformation("Dispositivo {DeviceId} → {State}", change.DeviceId, change.State);
            try
            {
                await hub.Clients.All.SendAsync(StatusHubMessages.DeviceStateChanged, change, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Invio SignalR fallito per il dispositivo {DeviceId}", change.DeviceId);
            }
        }

        return new AgentStatusAckDto(accepted, changes.Count, unknownDevices);
    }

    /// <summary>Presa in carico automatica al ripristino: chiude i problemi aperti precedenti al ripristino.</summary>
    private Task<int> ResolveOpenProblemsAsync(
        System.Linq.Expressions.Expression<Func<Event, bool>> problems, DateTimeOffset resolvedAt, CancellationToken ct) =>
        db.Events
            .Where(e => !e.Acknowledged && e.Time <= resolvedAt)
            .Where(problems)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Acknowledged, true)
                .SetProperty(e => e.ResolvedAt, resolvedAt), ct);

    // Valorizzato da TouchAgentAsync se l'agente era offline: i suoi eventi AgentOffline si chiudono dopo il salvataggio
    private DateTimeOffset? agentBackOnlineAt;

    private async Task TouchAgentAsync(string agentId, DateTimeOffset now, CancellationToken ct)
    {
        var agent = await db.Agents.FirstOrDefaultAsync(a => a.Id == agentId, ct);
        if (agent is null)
        {
            logger.LogInformation("Nuovo agente {AgentId}", agentId);
            db.Agents.Add(new Agent { Id = agentId, FirstSeen = now, LastSeen = now });
        }
        else
        {
            agent.LastSeen = now;
            if (agent.OfflineSince is { } offlineSince)
            {
                // Di nuovo online: evento AgentOnline, notificato come un ripristino
                agent.OfflineSince = null;
                agentBackOnlineAt = now;
                db.Events.Add(new Event
                {
                    AgentId = agentId,
                    Time = now,
                    Severity = EventSeverity.Info,
                    Acknowledged = true,
                    Type = EventTypes.AgentOnline,
                    Message = $"Agente {agentId} di nuovo online (offline per {AlertMessage.Duration(now - offlineSince)})",
                    NotifyState = NotifyState.Pending,
                    NotifyAfter = now
                });
                logger.LogInformation("Agente {AgentId} di nuovo online", agentId);
            }
        }
    }

    private async Task<int> NotifyDelaySecondsAsync(CancellationToken ct) =>
        await db.NotificationSettings.AsNoTracking()
            .Where(s => s.Id == NotificationSettings.SingletonId)
            .Select(s => (int?)s.DelaySeconds)
            .FirstOrDefaultAsync(ct) ?? 60;

    private static EventSeverity SeverityOf(NodeState state) => state switch
    {
        NodeState.Down => EventSeverity.Error,
        NodeState.Partial => EventSeverity.Warning,
        _ => EventSeverity.Info
    };
}
