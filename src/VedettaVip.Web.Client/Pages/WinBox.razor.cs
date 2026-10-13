// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using VedettaVip.Shared.Contracts;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Pages;

/// <summary>
/// Istruzioni e installer del gestore dei link (WinBox e PuTTY) per tutti gli utenti (i percorsi li imposta un Admin). La route
/// resta /winbox per i link già distribuiti.
/// </summary>
public partial class WinBox
{
    [Inject] private VedettaVipApiClient Api { get; set; } = default!;

    private RemoteAccessSettingsDto? settings;
    private string? error;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            settings = await Api.GetRemoteAccessSettingsAsync(CancellationToken.None);
        }
        catch (HttpRequestException ex)
        {
            error = $"Percorsi di WinBox e PuTTY non caricati ({ex.Message}): gli installer funzionano comunque.";
        }
    }
}
