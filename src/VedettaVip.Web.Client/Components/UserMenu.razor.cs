// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using VedettaVip.Shared.Contracts;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Components;

/// <summary>Utente corrente in alto a destra (nome, ruolo, Esci). Isola WebAssembly nel layout statico.</summary>
public partial class UserMenu
{
    [Inject] private VedettaVipApiClient Api { get; set; } = default!;
    [Inject] private CurrentUser User { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private CurrentUserDto? me;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            me = await User.GetAsync();
        }
        catch (HttpRequestException)
        {
            // API non raggiungibile: le pagine mostrano già l'errore
        }
    }

    private async Task LogoutAsync()
    {
        try
        {
            await Api.LogoutAsync(CancellationToken.None);
        }
        finally
        {
            User.Reset();
            Navigation.NavigateTo("login", forceLoad: true);
        }
    }
}
