// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Options;
using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;
using VedettaVip.Worker.Api;
using VedettaVip.Worker.Options;
using VedettaVip.Worker.Probes;
using VedettaVip.Worker.State;

namespace VedettaVip.Worker.Services;

/// <summary>
/// Raccolta di vicini e tabella ARP per la Discovery, inviata a POST /api/agent/neighbors (sostituisce la lettura precedente):
/// <list type="bullet">
/// <item>MikroTik con accesso API: /ip/neighbor (MNDP, CDP e LLDP già uniti dal router), /ip/arp e i lease DHCP;</item>
/// <item>altri device SNMP v2c: LLDP-MIB, CISCO-CDP-MIB e ipNetToMediaTable (ARP).</item>
/// </list>
/// Ogni Polling:NeighborDiscoveryMinutes per device, al primo avvistamento e subito su richiesta ("Scopri ora"
/// dall'hub agent). Device Down saltati; dopo un errore si riprova tra 5 minuti.
/// </summary>
public sealed class NeighborDiscoveryService(
    TargetProvider targets,
    DeviceStateStore states,
    IcmpProbe icmp,
    SnmpProbe snmp,
    AgentApiClient api,
    TimeProvider time,
    IOptions<PollingOptions> options,
    IOptions<AgentOptions> agentOptions,
    ILogger<NeighborDiscoveryService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryAfterError = TimeSpan.FromMinutes(5);
    private const int MaxParallel = 4;

    private readonly ConcurrentDictionary<Guid, DateTimeOffset> next = new();
    private readonly SemaphoreSlim wakeUp = new(0, 1);
    private volatile bool allRequested;

    /// <summary>Rilegge subito i vicini di tutti i device ("Scopri ora").</summary>
    public void RequestAll()
    {
        allRequested = true;
        try { wakeUp.Release(); } catch (SemaphoreFullException) { }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = MaxParallel, CancellationToken = stoppingToken };
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (targets.Current is { } list)
                {
                    var all = allRequested;
                    allRequested = false;
                    var now = time.GetUtcNow();
                    var due = list.Where(t => CanRead(t) && states.StateOf(t.DeviceId) != NodeState.Down
                                              && (all || !next.TryGetValue(t.DeviceId, out var at) || now >= at)).ToList();
                    if (due.Count > 0)
                        await Parallel.ForEachAsync(due, parallel, async (target, ct) => await DiscoverAsync(target, ct));
                }
                await wakeUp.WaitAsync(CheckInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private static bool CanRead(AgentTargetDto t) => t.RouterOs is not null || t.SnmpVersion == SnmpVersion.V2c;

    private async Task DiscoverAsync(AgentTargetDto target, CancellationToken ct)
    {
        var o = options.Value;
        List<NeighborDto>? neighbors = null;
        List<ArpEntryDto>? arp = null;
        try
        {
            (neighbors, arp) = target.RouterOs is { } ros
                ? await FromRouterOsAsync(target, ros, o, ct)
                : await FromSnmpAsync(target, o, ct);
            neighbors = neighbors?.Take(2000).ToList();
            arp = arp?.Take(20_000).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogDebug("{Name}: vicini non letti ({Message})", target.Name, ex.Message);
        }

        var ok = neighbors is not null
                 && await api.SendNeighborsAsync(new AgentNeighborsReportDto(agentOptions.Value.AgentId, target.DeviceId, neighbors, arp), ct);
        if (ok)
            logger.LogDebug("{Name}: {Count} vicini e {Arp} voci ARP inviati", target.Name, neighbors!.Count, arp?.Count ?? 0);
        next[target.DeviceId] = time.GetUtcNow() + (ok ? TimeSpan.FromMinutes(o.NeighborDiscoveryMinutes) : RetryAfterError);
    }

    private static async Task<(List<NeighborDto>, List<ArpEntryDto>?)> FromRouterOsAsync(
        AgentTargetDto target, RouterOsTargetDto ros, PollingOptions o, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(o.RouterOsTimeoutMs * 2); // ARP e lease possono essere migliaia di righe
        await using var client = await RouterOsApiClient.ConnectAsync(
            target.Address, ros.Port, ros.UseTls, ros.VerifyCertificate, ros.Username, ros.Password, timeout.Token);
        var neighbors = NeighborParsers.FromRouterOs((await client.RunAsync(["/ip/neighbor/print"], timeout.Token)).Rows);

        List<ArpEntryDto>? arp = null;
        try
        {
            var arpRows = (await client.RunAsync(["/ip/arp/print"], timeout.Token)).Rows;
            IReadOnlyList<IReadOnlyDictionary<string, string>> leases;
            try
            {
                leases = (await client.RunAsync(["/ip/dhcp-server/lease/print"], timeout.Token)).Rows;
            }
            catch (RouterOsApiException)
            {
                leases = []; // pacchetto DHCP assente o permesso negato: l'ARP basta
            }
            arp = NeighborParsers.FromRouterOsArp(arpRows, leases);
        }
        catch (RouterOsApiException)
        {
            // ARP non leggibile (permesso): restano i vicini
        }
        return (neighbors, arp);
    }

    /// <summary>LLDP, CDP e ARP via SNMP v2c; null se il device non risponde (si riproverà più tardi).</summary>
    private async Task<(List<NeighborDto>?, List<ArpEntryDto>?)> FromSnmpAsync(AgentTargetDto target, PollingOptions o, CancellationToken ct)
    {
        var address = await icmp.ResolveAsync(target.Address, TimeSpan.FromMilliseconds(o.IcmpTimeoutMs), ct);
        if (address is null)
            return (null, null);
        var community = target.SnmpCommunity ?? o.SnmpCommunity;
        var timeout = TimeSpan.FromMilliseconds(o.SnmpTimeoutMs);

        var remote = await snmp.WalkSubtreeAsync(address, community, NeighborParsers.LldpRemTable, timeout, ct);
        if (remote is null)
            return (null, null); // SNMP non risponde
        var result = new List<NeighborDto>();
        if (remote.Count > 0)
        {
            var addresses = await snmp.WalkSubtreeAsync(address, community, NeighborParsers.LldpRemManAddrTable, timeout, ct) ?? [];
            var ports = await snmp.WalkSubtreeAsync(address, community, NeighborParsers.LldpLocPortTable, timeout, ct) ?? [];
            result.AddRange(NeighborParsers.FromLldp(remote, addresses, ports));
        }
        var cdp = await snmp.WalkSubtreeAsync(address, community, NeighborParsers.CdpCacheTable, timeout, ct) ?? [];
        result.AddRange(NeighborParsers.FromCdp(cdp));

        // ARP solo dai router (uno switch L2 ha al più la propria voce): il tipo lo dice l'API
        List<ArpEntryDto>? arp = null;
        if (target.IsRouter)
        {
            var phys = await snmp.WalkSubtreeAsync(address, community, NeighborParsers.IpNetToMediaPhysAddress, timeout, ct);
            if (phys is not null)
                arp = NeighborParsers.FromSnmpArp(phys, await snmp.WalkSubtreeAsync(address, community, NeighborParsers.IpNetToMediaType, timeout, ct) ?? []);
        }
        return (result, arp);
    }
}
