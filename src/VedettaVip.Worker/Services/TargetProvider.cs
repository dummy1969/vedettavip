// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Threading.Channels;
using Microsoft.Extensions.Options;
using VedettaVip.Shared.Contracts;
using VedettaVip.Worker.Api;
using VedettaVip.Worker.Options;

namespace VedettaVip.Worker.Services;

/// <summary>
/// Ricarica i dispositivi da interrogare (GET /api/agent/targets): ogni TargetsRefreshSeconds e subito
/// quando <see cref="RequestRefresh"/> viene chiamato (notifica "TargetsChanged" dall'hub degli agenti).
/// Se l'API non risponde mantiene l'ultimo elenco valido; finché non ne ha uno riprova ogni 10 secondi.
/// </summary>
public sealed class TargetProvider(
    AgentApiClient api,
    IOptions<AgentOptions> options,
    ILogger<TargetProvider> logger) : BackgroundService
{
    private static readonly TimeSpan InitialRetry = TimeSpan.FromSeconds(10);

    // Al massimo una richiesta in sospeso: più notifiche ravvicinate producono un solo refresh
    private readonly SemaphoreSlim refreshRequested = new(0, 1);

    private readonly Channel<AgentTargetDto> changes = Channel.CreateUnbounded<AgentTargetDto>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

    private volatile IReadOnlyList<AgentTargetDto>? current;

    /// <summary>Null finché il primo caricamento non è riuscito.</summary>
    public IReadOnlyList<AgentTargetDto>? Current => current;

    /// <summary>
    /// Target comparsi o con indirizzo/versione SNMP cambiati rispetto al refresh precedente: vanno interrogati
    /// subito, con l'isteresi che riparte da zero. Il primo caricamento non produce cambi (ci pensa il ciclo normale).
    /// </summary>
    public ChannelReader<AgentTargetDto> Changes => changes.Reader;

    /// <summary>Anticipa il prossimo refresh (thread-safe, idempotente finché il refresh non parte).</summary>
    public void RequestRefresh()
    {
        try
        {
            refreshRequested.Release();
        }
        catch (SemaphoreFullException)
        {
            // Refresh già richiesto
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var refresh = TimeSpan.FromSeconds(options.Value.TargetsRefreshSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var targets = await api.GetTargetsAsync(stoppingToken);
                var previous = current;
                current = targets;

                if (previous is null || previous.Count != targets.Count)
                    logger.LogInformation("Target caricati: {Count} dispositivi", targets.Count);

                if (previous is not null)
                    PublishChanges(previous, targets);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Caricamento target fallito ({Message}); {Action}", ex.Message,
                    current is null ? "nessun elenco disponibile" : "uso l'ultimo elenco valido");
            }

            // Si sveglia allo scadere dell'intervallo oppure prima, se arriva una richiesta di refresh
            await refreshRequested.WaitAsync(current is null ? InitialRetry : refresh, stoppingToken);
        }
    }

    private void PublishChanges(IReadOnlyList<AgentTargetDto> previous, IReadOnlyList<AgentTargetDto> targets)
    {
        var before = previous.ToDictionary(t => t.DeviceId);
        foreach (var target in targets)
        {
            if (!before.TryGetValue(target.DeviceId, out var old))
            {
                logger.LogInformation("Nuovo target {Name} ({Address}): interrogato subito", target.Name, target.Address);
                changes.Writer.TryWrite(target);
            }
            else if (!string.Equals(old.Address, target.Address, StringComparison.OrdinalIgnoreCase)
                     || old.SnmpVersion != target.SnmpVersion)
            {
                logger.LogInformation("Target {Name} modificato ({OldAddress} {OldSnmp} → {Address} {Snmp}): stato ricalcolato da zero",
                    target.Name, old.Address, old.SnmpVersion, target.Address, target.SnmpVersion);
                changes.Writer.TryWrite(target);
            }
            else if (!string.Equals(old.SnmpCommunity, target.SnmpCommunity, StringComparison.Ordinal))
            {
                // Profilo SNMP cambiato (mai la community nel log): interrogato subito, così un Partial
                // dovuto alla community sbagliata rientra in un ciclo invece che al poll successivo
                logger.LogInformation("Target {Name}: credenziali SNMP cambiate, stato ricalcolato da zero", target.Name);
                changes.Writer.TryWrite(target);
            }
        }
    }

    public override void Dispose()
    {
        refreshRequested.Dispose();
        base.Dispose();
    }
}
