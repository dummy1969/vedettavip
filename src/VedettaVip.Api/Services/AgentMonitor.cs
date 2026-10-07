// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VedettaVip.Api.Data;
using VedettaVip.Api.Data.Entities;
using VedettaVip.Api.Options;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Services;

/// <summary>
/// Ogni 30 secondi marca offline gli agenti senza report da più di 2 intervalli di snapshot e crea l'evento
/// AgentOffline (notificato agli iscritti a "Tutto"). Il ritorno online è gestito da DeviceStatusService al primo report.
/// </summary>
public sealed class AgentMonitor(
    IServiceScopeFactory scopes,
    IOptions<AgentOptions> options,
    TimeProvider time,
    ILogger<AgentMonitor> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await CheckAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Controllo degli agenti offline fallito");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task CheckAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<VedettaVipDbContext>();
        var now = time.GetUtcNow();
        var limit = now - options.Value.StaleAfter;

        var stale = await db.Agents.Where(a => a.OfflineSince == null && a.LastSeen < limit).ToListAsync(ct);
        if (stale.Count == 0)
            return;

        foreach (var agent in stale)
        {
            agent.OfflineSince = now;
            db.Events.Add(new Event
            {
                AgentId = agent.Id,
                Time = now,
                Severity = EventSeverity.Error,
                Type = EventTypes.AgentOffline,
                Message = $"Agente {agent.Id} offline: nessun report dalle {agent.LastSeen:HH:mm:ss} UTC",
                NotifyState = NotifyState.Pending,
                NotifyAfter = now
            });
            logger.LogWarning("Agente {AgentId} offline (ultimo report {LastSeen:O})", agent.Id, agent.LastSeen);
        }

        await db.SaveChangesAsync(ct);
    }
}
