// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.ComponentModel.DataAnnotations;

namespace VedettaVip.Shared.Contracts;

/// <summary>Ruoli di VedettaVip (uno per utente).</summary>
public static class UserRoles
{
    /// <summary>Tutto, compresi utenti, clienti, contatti e impostazioni.</summary>
    public const string Admin = "Admin";
    /// <summary>Modifica mappe e dispositivi, prende in carico gli eventi.</summary>
    public const string Operator = "Operator";
    /// <summary>Solo consultazione (monitor NOC).</summary>
    public const string Viewer = "Viewer";

    public static readonly string[] All = [Admin, Operator, Viewer];

    public static bool CanEdit(string? role) => role is Admin or Operator;
}

/// <summary>Utente corrente (GET /api/auth/me). SetupRequired: nessun utente ancora, serve il primo amministratore.</summary>
public sealed record CurrentUserDto(bool Authenticated, bool SetupRequired, Guid? Id, string? UserName, string? DisplayName, string? Role);

public sealed record LoginDto(
    [property: Required(ErrorMessage = "Nome utente obbligatorio.")] string UserName,
    [property: Required(ErrorMessage = "Password obbligatoria.")] string Password,
    bool RememberMe = false);

/// <summary>Creazione del primo amministratore con il codice di setup scritto nel log dell'API.</summary>
public sealed record SetupDto(
    [property: Required(ErrorMessage = "Codice di setup obbligatorio.")] string SetupCode,
    [property: Required, StringLength(64, MinimumLength = 3, ErrorMessage = "Nome utente da 3 a 64 caratteri.")] string UserName,
    [property: Required, StringLength(128)] string DisplayName,
    [property: Required] string Password);

public sealed record ChangePasswordDto(
    [property: Required] string CurrentPassword,
    [property: Required] string NewPassword);

public sealed record UserDto(
    Guid Id,
    string UserName,
    string DisplayName,
    string? Email,
    string Role,
    bool Disabled,
    DateTimeOffset? LockedOutUntil,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

public sealed record UserCreateDto(
    [property: Required, StringLength(64, MinimumLength = 3, ErrorMessage = "Nome utente da 3 a 64 caratteri.")] string UserName,
    [property: Required, StringLength(128)] string DisplayName,
    [property: StringLength(256), EmailAddress(ErrorMessage = "Email non valida.")] string? Email,
    [property: Required] string Role,
    [property: Required] string Password);

public sealed record UserUpdateDto(
    [property: Required, StringLength(128)] string DisplayName,
    [property: StringLength(256), EmailAddress(ErrorMessage = "Email non valida.")] string? Email,
    [property: Required] string Role,
    bool Disabled);

public sealed record ResetPasswordDto([property: Required] string NewPassword);
