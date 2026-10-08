// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Shared.Contracts;

namespace VedettaVip.Web.Client.Services;

/// <summary>
/// Link "Apri con WinBox": winbox://&lt;indirizzo&gt;, aperto da un gestore installato sul PC dell'operatore (pagina /winbox).
/// Senza credenziali: WinBox chiede utente e password o usa i propri indirizzi salvati.
/// </summary>
public static class WinBoxLink
{
    /// <summary>Pagina con gli installer del gestore e le istruzioni.</summary>
    public const string HelpPage = "winbox";

    /// <summary>Link per un MikroTik con indirizzo; null per gli altri produttori.</summary>
    public static string? Href(DeviceVendor? vendor, string? address) =>
        vendor == DeviceVendor.MikroTik && !string.IsNullOrWhiteSpace(address) ? $"winbox://{address.Trim()}" : null;
}
