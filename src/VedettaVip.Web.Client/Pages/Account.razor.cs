// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using VedettaVip.Shared.Contracts;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Pages;

/// <summary>Dati dell'utente corrente, cambio password e verifica in due passaggi (app di autenticazione TOTP).</summary>
public partial class Account
{
    [Inject] private VedettaVipApiClient Api { get; set; } = default!;
    [Inject] private CurrentUser User { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private string? current;
    private string? newPassword;
    private string? newPassword2;
    private bool busy;
    private string? message;
    private bool isError;

    private TwoFactorStatusDto? twoFactor;
    private TwoFactorSetupDto? setup;
    private IReadOnlyList<string>? recoveryCodes;
    private bool copied;
    private string? code;
    private string? disablePassword;
    private string? tfaMessage;
    private bool tfaIsError;

    private string CodesDownloadHref => "data:text/plain;charset=utf-8," + Uri.EscapeDataString(CodesText);

    private string CodesText =>
        $"Codici di recupero VedettaVip ({User.Value?.UserName}), generati il {DateTime.Now:dd/MM/yyyy HH:mm}.\n" +
        "Ogni codice vale per un solo accesso.\n\n" + string.Join("\n", recoveryCodes ?? []) + "\n";

    protected override async Task OnInitializedAsync()
    {
        try
        {
            if (!(await User.GetAsync()).Authenticated)
            {
                ApiCredentialsHandler.RedirectToLogin(Navigation);
                return;
            }
            twoFactor = await Api.GetTwoFactorStatusAsync(CancellationToken.None);
            User.SetTwoFactorEnabled(twoFactor.Enabled);
        }
        catch (HttpRequestException ex)
        {
            (message, isError) = (ex.Message, true);
        }
    }

    private async Task ChangePasswordAsync()
    {
        if (newPassword != newPassword2)
        {
            (message, isError) = ("Le due password non coincidono.", true);
            return;
        }

        busy = true;
        try
        {
            await Api.ChangePasswordAsync(new ChangePasswordDto(current ?? "", newPassword ?? ""), CancellationToken.None);
            (message, isError) = ("Password cambiata.", false);
            current = newPassword = newPassword2 = null;
        }
        catch (HttpRequestException ex)
        {
            (message, isError) = (ex.Message, true);
        }
        finally
        {
            busy = false;
        }
    }

    // ---------- Verifica in due passaggi ----------

    private Task StartSetupAsync() => RunTwoFactorAsync(async () =>
    {
        setup = await Api.SetupTwoFactorAsync(CancellationToken.None);
        code = null;
        return null;
    });

    private void CancelSetup() => (setup, code, tfaMessage) = (null, null, null);

    private Task EnableAsync() => RunTwoFactorAsync(async () =>
    {
        ShowCodes(await Api.EnableTwoFactorAsync(code ?? "", CancellationToken.None));
        setup = null;
        return "Verifica in due passaggi attivata: dal prossimo accesso verrà chiesto il codice.";
    });

    private Task RegenerateAsync() => RunTwoFactorAsync(async () =>
    {
        ShowCodes(await Api.RegenerateRecoveryCodesAsync(code ?? "", CancellationToken.None));
        return "Nuovi codici di recupero generati: quelli vecchi non valgono più.";
    });

    private Task DisableAsync() => RunTwoFactorAsync(async () =>
    {
        await Api.DisableTwoFactorAsync(disablePassword ?? "", CancellationToken.None);
        return "Verifica in due passaggi disattivata. Le altre sessioni aperte con il tuo utente sono state chiuse.";
    });

    private Task ForgetBrowserAsync() => RunTwoFactorAsync(async () =>
    {
        await Api.ForgetTwoFactorBrowserAsync(CancellationToken.None);
        return "Al prossimo accesso da questo browser verrà chiesto il codice.";
    });

    private void ShowCodes(RecoveryCodesDto dto) => (recoveryCodes, copied, code) = (dto.Codes, false, null);

    private void CodesSaved() => recoveryCodes = null;

    private async Task CopyCodesAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", CodesText);
            copied = true;
        }
        catch (JSException)
        {
            (tfaMessage, tfaIsError) = ("Copia non consentita dal browser: usare \"Scarica .txt\".", true);
        }
    }

    /// <summary>Esegue un'azione (che restituisce il messaggio di esito) e ricarica lo stato.</summary>
    private async Task RunTwoFactorAsync(Func<Task<string?>> action)
    {
        busy = true;
        tfaMessage = null;
        try
        {
            var done = await action();
            twoFactor = await Api.GetTwoFactorStatusAsync(CancellationToken.None);
            User.SetTwoFactorEnabled(twoFactor.Enabled); // l'avviso "attiva la verifica" sparisce o ricompare
            (tfaMessage, tfaIsError) = (done, false);
            disablePassword = null;
        }
        catch (HttpRequestException ex)
        {
            (tfaMessage, tfaIsError) = (ex.Message, true);
            code = disablePassword = null;
        }
        finally
        {
            busy = false;
        }
    }
}
