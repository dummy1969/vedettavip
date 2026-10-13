// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;

namespace VedettaVip.Web.Client.Services;

/// <summary>
/// Link "Apri con WinBox": winbox://&lt;indirizzo&gt;[:porta], aperto da un gestore installato sul PC dell'operatore (pagina
/// /winbox), che passa la destinazione a WinBox così com'è. Porta solo se diversa dalla 8291 (<see cref="WinBoxTarget"/>).
/// Senza credenziali: WinBox chiede utente e password o usa i propri indirizzi salvati.
/// </summary>
public static class WinBoxLink
{
    /// <summary>Pagina con gli installer del gestore e le istruzioni.</summary>
    public const string HelpPage = "winbox";

    /// <summary>Link per un MikroTik con indirizzo; null per gli altri produttori. port = porta effettiva (null = 8291).</summary>
    public static string? Href(DeviceVendor? vendor, string? address, int? port) =>
        vendor == DeviceVendor.MikroTik && !string.IsNullOrWhiteSpace(address) ? $"winbox://{WinBoxTarget.Of(address, port)}" : null;

    /// <summary>" (porta 8292)" se il link porta una porta diversa da quella standard, altrimenti vuoto.</summary>
    public static string PortSuffix(string? address, int? port) =>
        address is not null && WinBoxTarget.Of(address, port) != address.Trim() ? $" (porta {port})" : "";
}
