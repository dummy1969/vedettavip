// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using VedettaVip.Shared.Contracts;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Pages;

/// <summary>
/// Accesso. Senza utenti mostra la creazione del primo amministratore (codice di setup dal log dell'API).
/// Dopo l'accesso ricarica l'app alla pagina di partenza: stato e connessioni ripartono con la sessione nuova.
/// </summary>
public partial class Login
{
    [Inject] private VedettaVipApiClient Api { get; set; } = default!;
    [Inject] private CurrentUser User { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [SupplyParameterFromQuery] public string? ReturnUrl { get; set; }

    private CurrentUserDto? me;
    private string? setupCode;
    private string? userName;
    private string? displayName;
    private string? password;
    private string? password2;
    private bool rememberMe;
    private bool busy;
    private string? error;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            User.Reset();
            me = await User.GetAsync();
            if (me.Authenticated)
                GoBack();
        }
        catch (HttpRequestException ex)
        {
            error = $"API non raggiungibile: {ex.Message}";
        }
    }

    private Task LoginAsync() => RunAsync(() =>
        Api.LoginAsync(new LoginDto(userName?.Trim() ?? "", password ?? "", rememberMe), CancellationToken.None));

    private Task SetupAsync()
    {
        if (password != password2)
        {
            error = "Le due password non coincidono.";
            return Task.CompletedTask;
        }
        return RunAsync(() => Api.SetupAsync(
            new SetupDto(setupCode ?? "", userName?.Trim() ?? "", (displayName ?? userName)?.Trim() ?? "", password ?? ""), CancellationToken.None));
    }

    private async Task RunAsync(Func<Task> action)
    {
        busy = true;
        error = null;
        try
        {
            await action();
            GoBack();
        }
        catch (HttpRequestException ex)
        {
            error = ex.Message; // credenziali errate, account bloccato, codice di setup errato, password troppo corta
            password = password2 = null;
        }
        finally
        {
            busy = false;
        }
    }

    /// <summary>Solo percorsi locali (niente redirect verso altri siti); ricarica completa dell'app.</summary>
    private void GoBack()
    {
        var target = ReturnUrl is { Length: > 1 } url && url.StartsWith('/') && !url.StartsWith("//") && !url.StartsWith("/login")
            ? url
            : "/map";
        Navigation.NavigateTo(target, forceLoad: true);
    }
}
