// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Shared.Models;

/// <summary>
/// Accessi di gestione dei dispositivi (puri, coperti da test): porte effettive e link.
/// <list type="bullet">
/// <item>web: http(s)://host[:porta]/ in una nuova scheda, nessun gestore sul PC;</item>
/// <item>terminale: vedettavip-ssh://host[:porta] o vedettavip-telnet://host[:porta], aperti con PuTTY dal gestore dei link
/// installato sul PC (schemi propri: nessun conflitto con altri programmi che gestiscono ssh:// o telnet://).</item>
/// </list>
/// Porta omessa se standard; IPv6 tra parentesi quadre. Nessuna credenziale nei link.
/// </summary>
public static partial class RemoteAccess
{
    public const string SshScheme = "vedettavip-ssh";
    public const string TelnetScheme = "vedettavip-telnet";

    public static int StandardPort(WebAccess web) => web == WebAccess.Https ? 443 : 80;

    public static int StandardPort(TerminalAccess terminal) => terminal == TerminalAccess.Telnet ? 23 : 22;

    /// <summary>Porta generale del protocollo (null = standard).</summary>
    public static int? DefaultPort(WebAccess web, RemoteAccessSettingsDto settings) => web switch
    {
        WebAccess.Http => settings.HttpPort,
        WebAccess.Https => settings.HttpsPort,
        _ => null
    };

    public static int? DefaultPort(TerminalAccess terminal, RemoteAccessSettingsDto settings) => terminal switch
    {
        TerminalAccess.Ssh => settings.SshPort,
        TerminalAccess.Telnet => settings.TelnetPort,
        _ => null
    };

    /// <summary>Accessi effettivi: porta del dispositivo, altrimenti quella generale del protocollo.</summary>
    public static DeviceAccessDto Resolve(int? winBoxPort, WebAccess web, int? webPort, TerminalAccess terminal, int? terminalPort,
        RemoteAccessSettingsDto settings) =>
        new(winBoxPort ?? settings.WinBoxPort,
            web, web == WebAccess.None ? null : webPort ?? DefaultPort(web, settings),
            terminal, terminal == TerminalAccess.None ? null : terminalPort ?? DefaultPort(terminal, settings));

    /// <summary>Pagina web di gestione ("https://192.0.2.1:8443/"); null senza accesso web o con un indirizzo non valido.</summary>
    public static string? WebUrl(string? address, WebAccess web, int? port)
    {
        if (web == WebAccess.None || Host(address) is not { } host)
            return null;
        var scheme = web == WebAccess.Https ? "https" : "http";
        return $"{scheme}://{host}{PortPart(port, StandardPort(web))}/";
    }

    /// <summary>Link per PuTTY ("vedettavip-ssh://192.0.2.1:2222"); null senza accesso da terminale o con un indirizzo non valido.</summary>
    public static string? TerminalHref(string? address, TerminalAccess terminal, int? port)
    {
        if (terminal == TerminalAccess.None || Host(address) is not { } host)
            return null;
        var scheme = terminal == TerminalAccess.Telnet ? TelnetScheme : SshScheme;
        return $"{scheme}://{host}{PortPart(port, StandardPort(terminal))}";
    }

    /// <summary>
    /// Indirizzo pronto per un URL: IPv4 o nome host (lettere, cifre, punti e trattini, iniziale alfanumerica) così com'è,
    /// IPv6 tra parentesi quadre; null se vuoto o con altri caratteri (lo stesso filtro dei gestori sul PC).
    /// </summary>
    public static string? Host(string? address)
    {
        var a = address?.Trim();
        if (string.IsNullOrEmpty(a))
            return null;
        if (IPAddress.TryParse(a, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6 && ip.ScopeId == 0)
            return $"[{ip}]";
        return HostName().IsMatch(a) ? a : null;
    }

    private static string PortPart(int? port, int standard) =>
        port is { } p && p != standard && p is >= 1 and <= 65535 ? $":{p}" : "";

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9.-]{0,252}$")]
    private static partial Regex HostName();
}
