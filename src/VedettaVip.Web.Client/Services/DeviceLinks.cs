// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;

namespace VedettaVip.Web.Client.Services;

/// <summary>
/// Link di accesso remoto di un dispositivo (menu del nodo, pagina Dispositivi), dalle porte effettive mandate dall'API:
/// <list type="bullet">
/// <item>"Apri con WinBox" (solo MikroTik): winbox://&lt;indirizzo&gt;[:porta] (<see cref="WinBoxTarget"/>);</item>
/// <item>"Apri interfaccia web": http(s)://indirizzo[:porta]/ in una nuova scheda;</item>
/// <item>"Apri con PuTTY": vedettavip-ssh:// o vedettavip-telnet:// (<see cref="RemoteAccess"/>).</item>
/// </list>
/// WinBox e PuTTY si aprono tramite il gestore dei link installato sul PC (pagina <see cref="HelpPage"/>). Nessuna credenziale.
/// </summary>
public static class DeviceLinks
{
    /// <summary>Pagina con gli installer del gestore dei link e le istruzioni.</summary>
    public const string HelpPage = "winbox";

    /// <summary>Link WinBox per un MikroTik con indirizzo; null per gli altri produttori. port = porta effettiva (null = 8291).</summary>
    public static string? WinBox(DeviceVendor? vendor, string? address, int? port) =>
        vendor == DeviceVendor.MikroTik && !string.IsNullOrWhiteSpace(address) ? $"winbox://{WinBoxTarget.Of(address, port)}" : null;

    public static string? Web(string? address, DeviceAccessDto? access) =>
        access is null ? null : RemoteAccess.WebUrl(address, access.Web, access.WebPort);

    public static string? Terminal(string? address, DeviceAccessDto? access) =>
        access is null ? null : RemoteAccess.TerminalHref(address, access.Terminal, access.TerminalPort);

    public static string WinBoxLabel(string? address, int? port) =>
        "Apri con WinBox" + (address is not null && WinBoxTarget.Of(address, port) != address.Trim() ? $" (porta {port})" : "");

    /// <summary>"Apri interfaccia web (HTTPS :8443)"; la porta solo se non standard.</summary>
    public static string WebLabel(DeviceAccessDto access) =>
        $"Apri interfaccia web ({(access.Web == WebAccess.Https ? "HTTPS" : "HTTP")}{PortText(access.WebPort, RemoteAccess.StandardPort(access.Web))})";

    /// <summary>"Apri con PuTTY (SSH :2222)"; la porta solo se non standard.</summary>
    public static string TerminalLabel(DeviceAccessDto access) =>
        $"Apri con PuTTY ({(access.Terminal == TerminalAccess.Telnet ? "Telnet" : "SSH")}{PortText(access.TerminalPort, RemoteAccess.StandardPort(access.Terminal))})";

    private static string PortText(int? port, int standard) => port is { } p && p != standard ? $" :{p}" : "";
}
