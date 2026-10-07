// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Shared.Contracts;

namespace VedettaVip.Web.Client.Services;

/// <summary>
/// Utente corrente (da GET /api/auth/me), caricato una volta per sessione dell'app WebAssembly. Le pagine lo usano per
/// mostrare o nascondere i comandi; i permessi veri li applica l'API.
/// </summary>
public sealed class CurrentUser(VedettaVipApiClient api)
{
    private Task<CurrentUserDto>? loading;

    public CurrentUserDto? Value { get; private set; }

    public bool IsAuthenticated => Value?.Authenticated == true;
    public bool IsAdmin => Value?.Role == UserRoles.Admin;
    /// <summary>Admin o Operatore: modifica mappe e dispositivi, prende in carico gli eventi.</summary>
    public bool CanEdit => UserRoles.CanEdit(Value?.Role);

    public async Task<CurrentUserDto> GetAsync(CancellationToken ct = default)
    {
        loading ??= api.GetMeAsync(ct);
        try
        {
            return Value = await loading;
        }
        catch
        {
            loading = null; // errore di rete: si riprova alla prossima richiesta
            throw;
        }
    }

    /// <summary>Dopo login, logout o cambio di ruolo.</summary>
    public void Reset() => (loading, Value) = (null, null);

    public static string RoleName(string? role) => role switch
    {
        UserRoles.Admin => "Amministratore",
        UserRoles.Operator => "Operatore",
        UserRoles.Viewer => "Sola lettura",
        _ => "-"
    };
}
