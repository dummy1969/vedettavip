// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using VedettaVip.Shared.Contracts;
using VedettaVip.Worker.Api;
using VedettaVip.Worker.Options;
using VedettaVip.Worker.Probes;

namespace VedettaVip.Worker.Services;

/// <summary>
/// Scansione di una subnet su richiesta (ScanRequested dall'hub agent): ping di tutti gli host (64 in parallelo), poi
/// sugli host vivi DNS inverso, SNMP v2c (sysName, sysDescr) con le community dei profili e qualche porta TCP tipica.
/// Avanzamento inviato ogni ~10 s, esito alla fine. Una scansione alla volta, le altre in coda.
/// </summary>
public sealed class SubnetScanService(
    IcmpProbe icmp,
    SnmpProbe snmp,
    AgentApiClient api,
    IOptions<PollingOptions> options,
    IOptions<AgentOptions> agentOptions,
    TimeProvider time,
    ILogger<SubnetScanService> logger) : BackgroundService
{
    /// <summary>
    /// SSH, Telnet, HTTP, HTTPS, RTSP (telecamere), RDP (Windows), API RouterOS 8728/8729, stampa raw 9100. Solo connessione
    /// TCP, nessun protocollo; Winbox (8291) escluso per scelta del progetto.
    /// </summary>
    public static readonly int[] Ports = [22, 23, 80, 443, 554, 3389, 8728, 8729, 9100];

    private static readonly TimeSpan PingTimeout = TimeSpan.FromMilliseconds(1000);
    private static readonly TimeSpan PortTimeout = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan DnsTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ProgressEvery = TimeSpan.FromSeconds(10);

    private readonly Channel<Guid> queue = Channel.CreateUnbounded<Guid>();

    public void Request(Guid scanId) => queue.Writer.TryWrite(scanId);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var scanId in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ScanAsync(scanId, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Scansione {ScanId} fallita", scanId);
                await api.SendScanResultAsync(scanId, new AgentScanResultDto(agentOptions.Value.AgentId, DiscoveryScanStatus.Failed, 0, 0, [], ex.Message), stoppingToken);
            }
        }
    }

    private async Task ScanAsync(Guid scanId, CancellationToken ct)
    {
        if (await api.GetScanAsync(scanId, ct) is not { } request)
            return; // scansione eliminata o già presa da un altro agente
        if (!Cidr.TryParse(request.Cidr, out var network, out var prefix))
        {
            await api.SendScanResultAsync(scanId, new AgentScanResultDto(agentOptions.Value.AgentId, DiscoveryScanStatus.Failed, 0, 0, [],
                $"rete non valida \"{request.Cidr}\" (IPv4, da /{Cidr.MinPrefix} a /32)"), ct);
            return;
        }

        var hosts = Cidr.Hosts(network, prefix).ToList();
        var communities = request.Communities.Append(new ScanCommunityDto(null, options.Value.SnmpCommunity)).DistinctBy(c => c.Community).ToList();
        var agentId = agentOptions.Value.AgentId;
        logger.LogInformation("Scansione di {Cidr}: {Count} indirizzi", request.Cidr, hosts.Count);

        var found = new ConcurrentBag<ScanHostDto>();
        var scanned = 0;
        var lastProgress = time.GetUtcNow();
        await api.SendScanResultAsync(scanId, new AgentScanResultDto(agentId, DiscoveryScanStatus.Running, 0, hosts.Count, []), ct);

        await Parallel.ForEachAsync(hosts, new ParallelOptions { MaxDegreeOfParallelism = 64, CancellationToken = ct }, async (ip, token) =>
        {
            var ping = await icmp.PingAsync(ip, PingTimeout, token);
            if (ping.Success)
                found.Add(await ProbeAsync(ip, ping.RttMs, communities, token));

            if (Interlocked.Increment(ref scanned) % 32 == 0 && time.GetUtcNow() - lastProgress > ProgressEvery)
            {
                lastProgress = time.GetUtcNow();
                await api.SendScanResultAsync(scanId, new AgentScanResultDto(agentId, DiscoveryScanStatus.Running, scanned, hosts.Count, []), token);
            }
        });

        var result = found.OrderBy(h => IPAddress.Parse(h.Address).GetAddressBytes(), Comparer<byte[]>.Create((a, b) => a.AsSpan().SequenceCompareTo(b))).ToList();
        await api.SendScanResultAsync(scanId, new AgentScanResultDto(agentId, DiscoveryScanStatus.Done, hosts.Count, hosts.Count, result), ct);
        logger.LogInformation("Scansione di {Cidr} completata: {Alive} host vivi su {Count}", request.Cidr, result.Count, hosts.Count);
    }

    /// <summary>Host vivo: DNS inverso, SNMP (prima community che risponde), porte TCP, tutto in parallelo.</summary>
    private async Task<ScanHostDto> ProbeAsync(IPAddress ip, double? rtt, IReadOnlyList<ScanCommunityDto> communities, CancellationToken ct)
    {
        var dns = ReverseDnsAsync(ip, ct);
        var ports = OpenPortsAsync(ip, ct);
        (string? SysName, string? SysDescr)? system = null;
        ScanCommunityDto? matched = null;
        var timeout = TimeSpan.FromMilliseconds(Math.Min(options.Value.SnmpTimeoutMs, 1500));
        foreach (var c in communities)
        {
            if (await snmp.GetSystemAsync(ip, c.Community, timeout, ct) is { } s)
            {
                (system, matched) = (s, c);
                break;
            }
        }
        return new ScanHostDto(ip.ToString(), rtt, await dns, Cut(system?.SysName, 128), Cut(system?.SysDescr, 512),
            matched?.ProfileId, matched is { ProfileId: null }, await ports);
    }

    private static async Task<string?> ReverseDnsAsync(IPAddress ip, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(DnsTimeout);
            var entry = await Dns.GetHostEntryAsync(ip.ToString(), timeout.Token);
            return entry.HostName != ip.ToString() ? Cut(entry.HostName, 255) : null;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private static async Task<List<int>> OpenPortsAsync(IPAddress ip, CancellationToken ct)
    {
        var open = await Task.WhenAll(Ports.Select(async port =>
        {
            using var tcp = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(PortTimeout);
            try
            {
                await tcp.ConnectAsync(ip, port, timeout.Token);
                return port;
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException && !ct.IsCancellationRequested)
            {
                return 0;
            }
        }));
        return open.Where(p => p > 0).ToList();
    }

    private static string? Cut(string? s, int max) => s is null ? null : s[..Math.Min(s.Length, max)];
}
