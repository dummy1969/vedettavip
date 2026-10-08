// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;

namespace VedettaVip.Web.Client.Components;

/// <summary>
/// Cornice delle pagine di Impostazioni (solo amministratori): sottomenu delle sezioni a sinistra, titolo e
/// azioni della pagina a destra. Le vecchie route (/users, /customers, /contacts) restano valide e attivano la stessa voce.
/// </summary>
public partial class SettingsShell
{
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [Parameter, EditorRequired] public string Title { get; set; } = "";
    /// <summary>Bottoni in alto a destra (es. "Nuovo utente").</summary>
    [Parameter] public RenderFragment? Actions { get; set; }
    [Parameter] public RenderFragment? ChildContent { get; set; }

    private sealed record Section(string Href, string Label, string Hint, params string[] Aliases);

    private static readonly Section[] Sections =
    [
        new("settings/monitoring", "Monitoraggio", "rilevazione, soglie", "settings"),
        new("settings/snmp", "Profili SNMP", "community per cliente"),
        new("settings/routeros", "Profili RouterOS", "API dei MikroTik"),
        new("settings/winbox", "WinBox", "percorso, gestore winbox://"),
        new("settings/notifications", "Notifiche", "email, Telegram, invio"),
        new("settings/contacts", "Contatti", "chi riceve cosa", "contacts"),
        new("settings/customers", "Clienti", "anagrafica, SNMP", "customers"),
        new("settings/users", "Utenti", "accessi e ruoli", "users"),
        new("settings/dashboard", "Dashboard", "pagina iniziale")
    ];

    private bool IsActive(Section s)
    {
        var path = Navigation.ToBaseRelativePath(Navigation.Uri).Split('?', '#')[0].TrimEnd('/');
        return string.Equals(path, s.Href, StringComparison.OrdinalIgnoreCase)
               || s.Aliases.Any(a => string.Equals(path, a, StringComparison.OrdinalIgnoreCase));
    }
}
