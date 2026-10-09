// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using VedettaVip.Shared.Contracts;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Pages;

/// <summary>
/// Gestione utenti (solo amministratori): crea, modifica ruolo, disattiva, reimposta password, azzera la verifica in due
/// passaggi, elimina.
/// </summary>
public partial class Users : IDisposable
{
    [Inject] private VedettaVipApiClient Api { get; set; } = default!;
    [Inject] private CurrentUser User { get; set; } = default!;

    private readonly CancellationTokenSource cts = new();
    private IReadOnlyList<UserDto>? users;
    private UserDto? editing;
    private bool formOpen;
    private bool busy;
    private string? error;
    private string? info;

    private string? userName;
    private string? displayName;
    private string? email;
    private string role = UserRoles.Viewer;
    private string? password;
    private bool disabled;
    private string? resetPassword;
    private bool confirmTwoFactorReset;

    protected override async Task OnInitializedAsync()
    {
        if ((await User.GetAsync()).Role == UserRoles.Admin)
            await RunAsync(() => Task.CompletedTask);
    }

    private void New()
    {
        (editing, formOpen, userName, displayName, email, role, password, disabled) =
            (null, true, null, null, null, UserRoles.Viewer, null, false);
    }

    private void Edit(UserDto u)
    {
        (editing, formOpen, displayName, email, role, disabled, resetPassword, confirmTwoFactorReset) =
            (u, true, u.DisplayName, u.Email, u.Role, u.Disabled, null, false);
    }

    private void Close() => (editing, formOpen, confirmTwoFactorReset) = (null, false, false);

    private Task SaveAsync() => RunAsync(async () =>
    {
        if (editing is null)
        {
            await Api.CreateUserAsync(new UserCreateDto(userName?.Trim() ?? "", (displayName ?? userName)?.Trim() ?? "",
                Blank(email), role, password ?? ""), cts.Token);
            info = $"Utente {userName} creato.";
        }
        else
        {
            await Api.UpdateUserAsync(editing.Id, new UserUpdateDto(displayName?.Trim() ?? "", Blank(email), role, disabled), cts.Token);
            info = $"Utente {editing.UserName} aggiornato.";
        }
        Close();
    });

    private Task ResetPasswordAsync() => RunAsync(async () =>
    {
        await Api.ResetUserPasswordAsync(editing!.Id, resetPassword ?? "", cts.Token);
        info = $"Password di {editing.UserName} reimpostata.";
        resetPassword = null;
    });

    /// <summary>Primo clic: chiede conferma; secondo clic: azzera.</summary>
    private Task ResetTwoFactorAsync()
    {
        if (!confirmTwoFactorReset)
        {
            confirmTwoFactorReset = true;
            return Task.CompletedTask;
        }
        return RunAsync(async () =>
        {
            await Api.ResetUserTwoFactorAsync(editing!.Id, cts.Token);
            info = $"Verifica in due passaggi di {editing.UserName} azzerata.";
            Close();
        });
    }

    private Task DeleteAsync(UserDto u) => RunAsync(async () =>
    {
        await Api.DeleteUserAsync(u.Id, cts.Token);
        info = $"Utente {u.UserName} eliminato.";
        if (editing?.Id == u.Id) Close();
    });

    private async Task RunAsync(Func<Task> action)
    {
        busy = true;
        error = null;
        info = null;
        try
        {
            await action();
            users = await Api.GetUsersAsync(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (HttpRequestException ex)
        {
            error = ex.Message; // password corta, nome duplicato, ultimo amministratore
        }
        finally
        {
            busy = false;
        }
    }

    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    public void Dispose()
    {
        cts.Cancel();
        cts.Dispose();
    }
}
