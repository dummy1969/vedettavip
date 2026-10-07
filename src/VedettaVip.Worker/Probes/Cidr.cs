// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Net;
using System.Net.Sockets;

namespace VedettaVip.Worker.Probes;

/// <summary>Reti IPv4 "a.b.c.d/n" per la scansione: indirizzi degli host (senza rete e broadcast fino a /30).</summary>
public static class Cidr
{
    /// <summary>Prefisso minimo ammesso: /22 = 1022 host (scansioni più ampie vanno spezzate).</summary>
    public const int MinPrefix = 22;

    public static bool TryParse(string text, out uint network, out int prefix)
    {
        (network, prefix) = (0, 0);
        var parts = text.Trim().Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var ip) || ip.AddressFamily != AddressFamily.InterNetwork
            || !int.TryParse(parts[1], out prefix) || prefix is < MinPrefix or > 32)
            return false;
        var mask = prefix == 0 ? 0 : uint.MaxValue << (32 - prefix);
        network = ToUInt(ip) & mask;
        return true;
    }

    public static IEnumerable<IPAddress> Hosts(uint network, int prefix)
    {
        var size = 1u << (32 - prefix);
        var (first, last) = prefix >= 31 ? (0u, size - 1) : (1u, size - 2);
        for (var i = first; i <= last; i++)
            yield return FromUInt(network + i);
    }

    private static uint ToUInt(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
    }

    private static IPAddress FromUInt(uint v) => new([(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v]);
}
