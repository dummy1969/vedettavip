// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using VedettaVip.Shared.Contracts;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Pages;

/// <summary>Dati dell'utente corrente e cambio password.</summary>
public partial class Account
{
    [Inject] private VedettaVipApiClient Api { get; set; } = default!;
    [Inject] private CurrentUser User { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    private string? current;
    private string? newPassword;
    private string? newPassword2;
    private bool busy;
    private string? message;
    private bool isError;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            if (!(await User.GetAsync()).Authenticated)
                ApiCredentialsHandler.RedirectToLogin(Navigation);
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
}
