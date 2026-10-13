// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Net;
using System.Net.Sockets;

namespace VedettaVip.Shared.Models;

/// <summary>
/// Destinazione passata a WinBox ("Connect To"): l'indirizzo, con ":porta" se diversa dalla 8291 (WinBox accetta
/// "192.168.88.1:9999" anche da riga di comando). Coperto da test.
/// </summary>
public static class WinBoxTarget
{
    /// <summary>Porta standard del servizio winbox su RouterOS: non serve indicarla.</summary>
    public const int DefaultPort = 8291;

    /// <summary>
    /// "indirizzo" o "indirizzo:porta". Porta omessa se assente o 8291, e per gli indirizzi IPv6: WinBox li vorrebbe tra
    /// parentesi quadre ("[2001:db8::1]:9999"), che i gestori winbox:// installati sui PC non accettano.
    /// </summary>
    public static string Of(string address, int? port)
    {
        var host = address.Trim();
        if (port is not { } p || p == DefaultPort || p is < 1 or > 65535)
            return host;
        if (IPAddress.TryParse(host, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6)
            return host;
        return $"{host}:{p}";
    }

    /// <summary>Porta effettiva: quella del dispositivo, altrimenti la generale (null = 8291).</summary>
    public static int? Effective(int? devicePort, int? defaultPort) => devicePort ?? defaultPort;

    /// <summary>True se l'indirizzo è IPv6 (la porta non può essere passata: vedi <see cref="Of"/>).</summary>
    public static bool IsIPv6(string? address) =>
        IPAddress.TryParse(address?.Trim(), out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6;
}
