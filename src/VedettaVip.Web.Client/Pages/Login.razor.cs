// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using VedettaVip.Shared.Contracts;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Pages;

/// <summary>
/// Accesso. Senza utenti mostra la creazione del primo amministratore (codice di setup dal log dell'API).
/// Con la verifica in due passaggi attiva, dopo la password chiede il codice dell'app (o un codice di recupero).
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
    private bool twoFactor;
    private bool useRecoveryCode;
    private bool rememberBrowser;
    private string? code;
    private string? recoveryCode;
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

    private Task LoginAsync() => RunAsync(async () =>
    {
        var result = await Api.LoginAsync(new LoginDto(userName?.Trim() ?? "", password ?? "", rememberMe), CancellationToken.None);
        password = null;
        if (!result.RequiresTwoFactor)
            return true;
        (twoFactor, useRecoveryCode, code, recoveryCode) = (true, false, null, null);
        return false;
    });

    private Task TwoFactorAsync() => RunAsync(async () =>
    {
        await Api.LoginTwoFactorAsync(new TwoFactorLoginDto(code ?? "", rememberMe, rememberBrowser), CancellationToken.None);
        return true;
    });

    private Task RecoveryCodeAsync() => RunAsync(async () =>
    {
        await Api.LoginRecoveryCodeAsync(new RecoveryCodeLoginDto(recoveryCode ?? ""), CancellationToken.None);
        return true;
    });

    private void SwitchCode(bool recovery) => (useRecoveryCode, error, code, recoveryCode) = (recovery, null, null, null);

    /// <summary>Codice scaduto (oltre 5 minuti) o utente sbagliato: si riparte dalla password.</summary>
    private void Restart() => (twoFactor, error, code, recoveryCode) = (false, null, null, null);

    private Task SetupAsync()
    {
        if (password != password2)
        {
            error = "Le due password non coincidono.";
            return Task.CompletedTask;
        }
        return RunAsync(async () =>
        {
            await Api.SetupAsync(
                new SetupDto(setupCode ?? "", userName?.Trim() ?? "", (displayName ?? userName)?.Trim() ?? "", password ?? ""), CancellationToken.None);
            return true;
        });
    }

    /// <summary>Esegue un passo dell'accesso; true = accesso completato, si torna alla pagina di partenza.</summary>
    private async Task RunAsync(Func<Task<bool>> action)
    {
        busy = true;
        error = null;
        try
        {
            if (await action())
                GoBack();
        }
        catch (HttpRequestException ex)
        {
            // credenziali o codice errati, account bloccato, codice di setup errato, password troppo corta
            error = ex.Message;
            password = password2 = code = recoveryCode = null;
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
