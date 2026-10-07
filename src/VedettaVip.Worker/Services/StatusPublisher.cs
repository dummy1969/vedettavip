// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.Extensions.Options;
using VedettaVip.Shared.Contracts;
using VedettaVip.Worker.Api;
using VedettaVip.Worker.Options;
using VedettaVip.Worker.State;

namespace VedettaVip.Worker.Services;

/// <summary>
/// Invia all'API i cambi di stato appena rilevati e uno snapshot completo ogni SnapshotIntervalSeconds.
/// Se l'API non risponde i cambi restano in un buffer in memoria (ordinato, limitato a BufferCapacity:
/// oltre si scartano i più vecchi, lo snapshot successivo riallinea comunque l'API) e si riprova con
/// backoff esponenziale con jitter. L'API è idempotente: ritrasmettere un report già elaborato non
/// genera eventi doppi.
/// </summary>
public sealed class StatusPublisher(
    AgentApiClient api,
    DeviceStateStore store,
    TimeProvider time,
    IOptions<AgentOptions> options,
    ILogger<StatusPublisher> logger) : BackgroundService
{
    private readonly Lock gate = new();
    private readonly Queue<(long Seq, DeviceStatusResultDto Result)> pending = new();
    private readonly SemaphoreSlim signal = new(0);
    private long nextSeq;
    private long dropped;

    public void EnqueueChange(DeviceStatusResultDto result)
    {
        var o = options.Value;
        lock (gate)
        {
            if (pending.Count >= o.BufferCapacity)
            {
                pending.Dequeue();
                if (dropped++ % 1000 == 0)
                    logger.LogWarning("Buffer dei cambi di stato pieno ({Capacity}): scartati i più vecchi ({Dropped} finora)",
                        o.BufferCapacity, dropped);
            }
            pending.Enqueue((nextSeq++, result));
        }
        signal.Release();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        var snapshotInterval = TimeSpan.FromSeconds(o.SnapshotIntervalSeconds);
        var nextSnapshot = time.GetUtcNow() + snapshotInterval;
        var snapshotDue = false;
        var attempt = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = time.GetUtcNow();
            if (now >= nextSnapshot)
            {
                snapshotDue = true; // se l'API è giù resta in coda un solo snapshot
                nextSnapshot = now + snapshotInterval;
            }

            var batch = PeekBatch(o.MaxBatchSize);
            SendOutcome outcome;

            if (batch.Count > 0)
            {
                outcome = await SendAsync(batch.Select(b => b.Result).ToList(), isSnapshot: false, stoppingToken);
                if (outcome is SendOutcome.Sent or SendOutcome.Rejected)
                    RemoveUpTo(batch[^1].Seq);
            }
            else if (snapshotDue)
            {
                var snapshot = store.Snapshot();
                outcome = snapshot.Count == 0 ? SendOutcome.Sent : await SendAsync(snapshot, isSnapshot: true, stoppingToken);
                if (outcome is SendOutcome.Sent or SendOutcome.Rejected)
                    snapshotDue = false;
            }
            else
            {
                // Niente da inviare: attende un nuovo cambio o la scadenza dello snapshot
                var wait = nextSnapshot - time.GetUtcNow();
                if (wait > TimeSpan.Zero)
                    await signal.WaitAsync(wait, stoppingToken);
                continue;
            }

            if (outcome is SendOutcome.Sent or SendOutcome.Rejected)
            {
                attempt = 0;
                continue;
            }

            if (outcome == SendOutcome.Unauthorized)
                logger.LogError("API: chiave agente rifiutata (401). Verificare Agent:ApiKey su Worker e API");

            var delay = Backoff(outcome == SendOutcome.Unauthorized ? int.MaxValue : attempt++, o);
            logger.LogInformation("Nuovo tentativo di invio tra {Delay:N1} s ({Pending} cambi in attesa)", delay.TotalSeconds, PendingCount());
            await Task.Delay(delay, time, stoppingToken);
        }
    }

    private async Task<SendOutcome> SendAsync(IReadOnlyList<DeviceStatusResultDto> results, bool isSnapshot, CancellationToken ct)
    {
        var outcome = await api.SendStatusAsync(new AgentStatusReportDto(options.Value.AgentId, isSnapshot, results), ct);
        if (outcome == SendOutcome.Sent)
            logger.LogDebug("Inviato {Kind} con {Count} risultati", isSnapshot ? "snapshot" : "cambi", results.Count);
        return outcome;
    }

    private List<(long Seq, DeviceStatusResultDto Result)> PeekBatch(int max)
    {
        lock (gate)
            return pending.Take(max).ToList();
    }

    /// <summary>Rimuove gli elementi inviati; per numero di sequenza, perché nel frattempo il buffer pieno può averne scartati.</summary>
    private void RemoveUpTo(long seq)
    {
        lock (gate)
        {
            while (pending.Count > 0 && pending.Peek().Seq <= seq)
                pending.Dequeue();
        }
    }

    private int PendingCount()
    {
        lock (gate)
            return pending.Count;
    }

    private static TimeSpan Backoff(int attempt, AgentOptions o)
    {
        var seconds = Math.Min(o.RetryMaxSeconds, o.RetryInitialSeconds * Math.Pow(2, Math.Min(attempt, 30)));
        return TimeSpan.FromSeconds(seconds * (0.8 + Random.Shared.NextDouble() * 0.4));
    }
}
