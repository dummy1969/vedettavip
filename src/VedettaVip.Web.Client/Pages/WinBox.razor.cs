// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using VedettaVip.Shared.Contracts;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Pages;

/// <summary>Istruzioni e installer del gestore dei link winbox:// per tutti gli utenti (i percorsi li imposta un Admin).</summary>
public partial class WinBox
{
    [Inject] private VedettaVipApiClient Api { get; set; } = default!;

    private WinBoxSettingsDto? settings;
    private string? error;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            settings = await Api.GetWinBoxSettingsAsync(CancellationToken.None);
        }
        catch (HttpRequestException ex)
        {
            error = $"Percorsi di WinBox non caricati ({ex.Message}): gli installer funzionano comunque.";
        }
    }
}
