// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Components;

/// <summary>
/// Mostra il contenuto solo agli amministratori (agli altri un avviso; senza sessione porta all'accesso).
/// È solo interfaccia: i permessi veri li applica l'API.
/// </summary>
public partial class AdminOnly
{
    private enum State { Loading, Allowed, Denied }

    [Inject] private CurrentUser User { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [Parameter] public RenderFragment? ChildContent { get; set; }

    private State state;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var me = await User.GetAsync();
            if (!me.Authenticated)
                ApiCredentialsHandler.RedirectToLogin(Navigation);
            state = User.IsAdmin ? State.Allowed : State.Denied;
        }
        catch (HttpRequestException)
        {
            state = State.Denied;
        }
    }
}
