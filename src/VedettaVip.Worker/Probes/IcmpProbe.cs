// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace VedettaVip.Worker.Probes;

public readonly record struct IcmpResult(bool Success, double? RttMs);

/// <summary>
/// Un echo ICMP per poll. Su Linux .NET usa socket raw (serve CAP_NET_RAW, nel container
/// cap_add: NET_RAW) o socket ICMP non privilegiati se net.ipv4.ping_group_range lo consente.
/// </summary>
public sealed class IcmpProbe(ILogger<IcmpProbe> logger)
{
    public async Task<IcmpResult> PingAsync(IPAddress address, TimeSpan timeout, CancellationToken ct)
    {
        using var ping = new Ping();
        try
        {
            var reply = await ping.SendPingAsync(address, timeout, cancellationToken: ct);
            return reply.Status == IPStatus.Success
                ? new IcmpResult(true, reply.RoundtripTime)
                : new IcmpResult(false, null);
        }
        catch (PingException ex)
        {
            logger.LogDebug(ex, "Ping verso {Address} fallito", address);
            return new IcmpResult(false, null);
        }
    }

    /// <summary>Risolve IP o hostname (preferendo IPv4). Null se la risoluzione fallisce: conta come ICMP fallito.</summary>
    public async Task<IPAddress?> ResolveAsync(string address, TimeSpan timeout, CancellationToken ct)
    {
        if (IPAddress.TryParse(address, out var ip))
            return ip;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(address, cts.Token);
            return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses.FirstOrDefault();
        }
        catch (Exception ex) when (ex is SocketException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogDebug("Risoluzione DNS di {Address} fallita: {Message}", address, ex.Message);
            return null;
        }
    }
}
