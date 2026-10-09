// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Components;

/// <summary>
/// Avviso in cima alle pagine per chi non ha attivato la verifica in due passaggi, con le istruzioni. "Più tardi" lo nasconde
/// fino alla chiusura del browser (sessionStorage), "Non mostrare più" su questo browser (localStorage, per utente).
/// Sparisce quando la verifica viene attivata da Account (<see cref="CurrentUser.Changed"/>). Isola WebAssembly nel layout statico.
/// </summary>
public partial class TwoFactorReminder : IDisposable
{
    [Inject] private CurrentUser User { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private bool dismissed;
    private bool visible;

    private string Key => $"vedettavip.tfaReminder.{User.Value?.Id}";

    protected override async Task OnInitializedAsync()
    {
        User.Changed += OnUserChanged;
        Navigation.LocationChanged += OnLocationChanged;
        try
        {
            var me = await User.GetAsync();
            if (!me.Authenticated || me.TwoFactorEnabled)
                return;
            dismissed = await ReadAsync("localStorage") is not null || await ReadAsync("sessionStorage") is not null;
        }
        catch (HttpRequestException)
        {
            // API non raggiungibile: le pagine mostrano già l'errore
        }
        Update();
    }

    /// <summary>Visibile se l'utente è autenticato senza verifica, non l'ha nascosto e non è già su Account o Accedi.</summary>
    private void Update()
    {
        var page = Navigation.ToBaseRelativePath(Navigation.Uri).Split('?', '#')[0].ToLowerInvariant();
        visible = User.Value is { Authenticated: true, TwoFactorEnabled: false } && !dismissed && page is not ("account" or "login");
    }

    private Task LaterAsync() => DismissAsync("sessionStorage");

    private Task NeverAsync() => DismissAsync("localStorage");

    private async Task DismissAsync(string storage)
    {
        dismissed = true;
        Update();
        try
        {
            await JS.InvokeVoidAsync($"{storage}.setItem", Key, "off");
        }
        catch (JSException)
        {
            // storage non disponibile (navigazione privata con restrizioni): nascosto solo fino al ricaricamento
        }
    }

    private async Task<string?> ReadAsync(string storage)
    {
        try
        {
            return await JS.InvokeAsync<string?>($"{storage}.getItem", Key);
        }
        catch (JSException)
        {
            return null;
        }
    }

    private void OnUserChanged() => InvokeAsync(() => { Update(); StateHasChanged(); });

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e) => InvokeAsync(() => { Update(); StateHasChanged(); });

    public void Dispose()
    {
        User.Changed -= OnUserChanged;
        Navigation.LocationChanged -= OnLocationChanged;
    }
}
