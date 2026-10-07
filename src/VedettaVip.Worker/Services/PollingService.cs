// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Collections.Concurrent;
using System.Diagnostics;
using Lextm.SharpSnmpLib;
using Microsoft.Extensions.Options;
using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;
using VedettaVip.Worker.Api;
using VedettaVip.Worker.Options;
using VedettaVip.Worker.Probes;
using VedettaVip.Worker.State;

namespace VedettaVip.Worker.Services;

/// <summary>
/// Ciclo di polling: ogni IntervalSeconds interroga tutti i target in parallelo (limitato), aggiorna
/// l'isteresi e passa i cambi di stato allo <see cref="StatusPublisher"/>. I target nuovi o modificati
/// segnalati dal <see cref="TargetProvider"/> vengono interrogati subito, senza aspettare il ciclo.
/// Il traffico delle interfacce (SNMP v2c) viene calcolato a ogni poll e inviato all'API a fine ciclo.
/// </summary>
public sealed class PollingService(
    TargetProvider targets,
    IcmpProbe icmp,
    SnmpProbe snmp,
    DeviceStateStore store,
    StatusPublisher publisher,
    TrafficCalculator traffic,
    AgentApiClient api,
    TimeProvider time,
    IOptions<PollingOptions> options,
    IOptions<AgentOptions> agentOptions,
    ILogger<PollingService> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, DeviceStateTracker> trackers = new();
    private readonly ConcurrentDictionary<Guid, byte> v3Warned = new();
    private readonly ConcurrentDictionary<Guid, byte> v1TrafficWarned = new();
    // Campioni di traffico calcolati durante il ciclo (e dai poll immediati), inviati a fine ciclo
    private readonly ConcurrentQueue<InterfaceTrafficDto> trafficSamples = new();
    // Esito dei ping per lo storico di latenza e perdita, inviati insieme al traffico
    private readonly ConcurrentQueue<DevicePingDto> pingSamples = new();
    // Dispositivi in corso di interrogazione: mai due poll in parallelo sullo stesso (ciclo + poll immediato)
    private readonly ConcurrentDictionary<Guid, byte> inFlight = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        // Token comune: se il ciclo termina (anche per errore) si ferma anche il poll immediato
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var immediate = PollChangesAsync(o, stop.Token);
        try
        {
            await RunCyclesAsync(o, stop.Token);
        }
        finally
        {
            await stop.CancelAsync();
            await immediate;
        }
    }

    private async Task RunCyclesAsync(PollingOptions o, CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(o.IntervalSeconds);
        using var timer = new PeriodicTimer(interval, time);

        do
        {
            var list = targets.Current;
            if (list is null)
            {
                logger.LogDebug("Nessun target disponibile: ciclo saltato");
                continue;
            }

            RemoveStaleTrackers(list);

            var sw = Stopwatch.StartNew();
            var parallel = new ParallelOptions { MaxDegreeOfParallelism = o.MaxDegreeOfParallelism, CancellationToken = stoppingToken };
            await Parallel.ForEachAsync(list, parallel, async (target, ct) => await TryPollAsync(target, o, resetState: false, ct));
            sw.Stop();

            await SendTrafficAsync(stoppingToken);

            if (sw.Elapsed > interval)
                logger.LogWarning("Ciclo di polling di {Elapsed:N1} s oltre l'intervallo di {Interval} s ({Count} target): " +
                                  "aumentare Polling:MaxDegreeOfParallelism", sw.Elapsed.TotalSeconds, o.IntervalSeconds, list.Count);
            else
                logger.LogDebug("Ciclo di polling completato in {Elapsed} ms ({Count} target)", sw.ElapsedMilliseconds, list.Count);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>
    /// Poll immediato dei target nuovi o con indirizzo/SNMP cambiati: l'isteresi riparte da zero (Unknown),
    /// quindi un dispositivo raggiungibile diventa Up al secondo ping riuscito, cioè al ciclo successivo.
    /// </summary>
    private async Task PollChangesAsync(PollingOptions o, CancellationToken stoppingToken)
    {
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = o.MaxDegreeOfParallelism, CancellationToken = stoppingToken };
        try
        {
            await Parallel.ForEachAsync(targets.Changes.ReadAllAsync(stoppingToken), parallel, async (target, ct) =>
            {
                // Se il ciclo normale lo sta interrogando, aspetta che finisca (al massimo i timeout dei probe)
                while (!await TryPollAsync(target, o, resetState: true, ct))
                    await Task.Delay(TimeSpan.FromMilliseconds(200), time, ct);
            });
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Interroga il target; false se è già in corso un altro poll dello stesso dispositivo.</summary>
    private async ValueTask<bool> TryPollAsync(AgentTargetDto target, PollingOptions o, bool resetState, CancellationToken ct)
    {
        if (!inFlight.TryAdd(target.DeviceId, 0))
            return false;

        try
        {
            if (resetState)
            {
                trackers.TryRemove(target.DeviceId, out _);
                traffic.Reset(target.DeviceId);
            }

            await PollAsync(target, o, ct);
            return true;
        }
        finally
        {
            inFlight.TryRemove(target.DeviceId, out _);
        }
    }

    private async ValueTask PollAsync(AgentTargetDto target, PollingOptions o, CancellationToken ct)
    {
        var thresholds = ThresholdsOf(target, o);
        var tracker = trackers.GetOrAdd(target.DeviceId, _ => new DeviceStateTracker(thresholds));
        tracker.Thresholds = thresholds; // cambiate dalla UI: valgono dal poll corrente, senza azzerare i conteggi
        var before = tracker.State;

        try
        {
            var icmpTimeout = TimeSpan.FromMilliseconds(o.IcmpTimeoutMs);
            var address = await icmp.ResolveAsync(target.Address, icmpTimeout, ct);
            var ping = address is null ? default : await icmp.PingAsync(address, icmpTimeout, ct);
            // DNS non risolto = ping perso: per chi guarda il grafico il device non era raggiungibile
            pingSamples.Enqueue(new DevicePingDto(target.DeviceId, time.GetUtcNow(), ping.Success, ping.Success ? ping.RttMs : null));

            bool? snmpOk = null;
            if (tracker.RegisterIcmp(ping.Success) && SnmpVersionOf(target) is { } version)
            {
                var ifIndexes = InterfacesToMeasure(target, version);
                var poll = await snmp.PollAsync(address!, version, target.SnmpCommunity ?? o.SnmpCommunity, ifIndexes, TimeSpan.FromMilliseconds(o.SnmpTimeoutMs), ct);
                snmpOk = poll.Ok;
                tracker.RegisterSnmp(poll.Ok);

                var now = time.GetUtcNow();
                foreach (var counters in poll.Interfaces)
                    if (traffic.Add(target.DeviceId, counters, now) is { } sample)
                        trafficSamples.Enqueue(sample);
            }

            var result = new DeviceStatusResultDto(target.DeviceId, tracker.State, time.GetUtcNow(), ping.RttMs, snmpOk);
            store.Set(result);

            if (tracker.State != before && tracker.State != NodeState.Unknown)
            {
                logger.LogInformation("{Name} ({Address}): {Before} → {After}", target.Name, target.Address, before, tracker.State);
                publisher.EnqueueChange(result);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Un probe che esplode non deve fermare il ciclo degli altri dispositivi
            logger.LogError(ex, "Errore nel polling di {Name} ({Address})", target.Name, target.Address);
        }
    }

    /// <summary>V1 e V2c usano la community di configurazione; V3 non è supportato finché non esistono i profili di credenziali.</summary>
    public static VersionCode? SnmpVersionCode(SnmpVersion version) => version switch
    {
        SnmpVersion.V1 => VersionCode.V1,
        SnmpVersion.V2c => VersionCode.V2,
        _ => null // None e V3: solo ICMP, quindi mai Partial
    };

    private VersionCode? SnmpVersionOf(AgentTargetDto target)
    {
        if (target.SnmpVersion == SnmpVersion.V3 && v3Warned.TryAdd(target.DeviceId, 0))
            logger.LogWarning("{Name}: SNMPv3 non ancora supportato, verifico solo ICMP", target.Name);
        return SnmpVersionCode(target.SnmpVersion);
    }

    /// <summary>Soglie effettive inviate dall'API (generali o specifiche del device); in mancanza quelle locali (Polling:*).</summary>
    private static StateThresholds ThresholdsOf(AgentTargetDto target, PollingOptions o) => target.Thresholds is { } t
        ? new StateThresholds(t.DownAfterFailures, t.UpAfterSuccesses, t.SnmpDegradedAfterFailures)
        : new StateThresholds(o.DownAfterFailures, o.UpAfterSuccesses, o.SnmpDegradedAfterFailures);

    /// <summary>I contatori a 64 bit (ifHC*) esistono solo da SNMP v2c: in v1 niente traffico.</summary>
    private IReadOnlyList<int> InterfacesToMeasure(AgentTargetDto target, VersionCode version)
    {
        if (version != VersionCode.V1 || target.IfIndexes.Count == 0)
            return target.IfIndexes;

        if (v1TrafficWarned.TryAdd(target.DeviceId, 0))
            logger.LogWarning("{Name}: il traffico dei link richiede SNMP v2c (contatori a 64 bit), il device è in v1", target.Name);
        return [];
    }

    /// <summary>
    /// Invia traffico e ping del ciclo. Senza buffer né ritentativi: se l'API non risponde si perde un punto dello
    /// storico (30 s), mentre stato e cambi di stato hanno il loro canale con buffer (StatusPublisher).
    /// </summary>
    private async Task SendTrafficAsync(CancellationToken ct)
    {
        var samples = Drain(trafficSamples);
        var pings = Drain(pingSamples);

        if (samples.Count > 0 || pings.Count > 0)
            await api.SendTrafficAsync(new AgentTrafficReportDto(agentOptions.Value.AgentId, samples, pings), ct);
    }

    private static List<T> Drain<T>(ConcurrentQueue<T> queue)
    {
        var items = new List<T>();
        while (queue.TryDequeue(out var item))
            items.Add(item);
        return items;
    }

    private void RemoveStaleTrackers(IReadOnlyList<AgentTargetDto> list)
    {
        traffic.RetainOnly(list);
        var ids = list.Select(t => t.DeviceId).ToHashSet();
        foreach (var id in trackers.Keys.Where(id => !ids.Contains(id)))
            trackers.TryRemove(id, out _);
        store.RemoveExcept(ids);
    }
}
