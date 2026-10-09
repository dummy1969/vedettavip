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

/// <summary>
/// Utente corrente (GET /api/auth/me). SetupRequired: nessun utente ancora, serve il primo amministratore.
/// TwoFactorEnabled: verifica in due passaggi attiva (senza, la UI propone di attivarla).
/// </summary>
public sealed record CurrentUserDto(
    bool Authenticated, bool SetupRequired, Guid? Id, string? UserName, string? DisplayName, string? Role, bool TwoFactorEnabled = false);

public sealed record LoginDto(
    [property: Required(ErrorMessage = "Nome utente obbligatorio.")] string UserName,
    [property: Required(ErrorMessage = "Password obbligatoria.")] string Password,
    bool RememberMe = false);

/// <summary>Esito della password (POST /api/auth/login): con la verifica in due passaggi attiva serve ancora il codice.</summary>
public sealed record LoginResultDto(bool RequiresTwoFactor);

/// <summary>Secondo passo del login con il codice dell'app di autenticazione (TOTP).</summary>
public sealed record TwoFactorLoginDto(
    [property: Required(ErrorMessage = "Codice obbligatorio.")] string Code,
    bool RememberMe = false,
    bool RememberBrowser = false);

/// <summary>Secondo passo del login con un codice di recupero (monouso).</summary>
public sealed record RecoveryCodeLoginDto(
    [property: Required(ErrorMessage = "Codice di recupero obbligatorio.")] string RecoveryCode);

/// <summary>Verifica in due passaggi (TOTP, RFC 6238) dell'utente corrente.</summary>
public static class TwoFactorDefaults
{
    /// <summary>Giorni per cui un browser "ricordato" non chiede il codice.</summary>
    public const int RememberBrowserDays = 7;
    public const int RecoveryCodeCount = 10;
}

public sealed record TwoFactorStatusDto(bool Enabled, int RecoveryCodesLeft, bool BrowserRemembered);

/// <summary>Chiave da registrare nell'app: in chiaro (a gruppi di 4), come URI otpauth:// e come QR code (data URI SVG).</summary>
public sealed record TwoFactorSetupDto(string SharedKey, string AuthenticatorUri, string QrCodeDataUri);

public sealed record TwoFactorCodeDto([property: Required(ErrorMessage = "Codice obbligatorio.")] string Code);

public sealed record TwoFactorDisableDto([property: Required(ErrorMessage = "Password obbligatoria.")] string Password);

/// <summary>Codici di recupero nuovi: mostrati una volta sola.</summary>
public sealed record RecoveryCodesDto(IReadOnlyList<string> Codes);

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
    DateTimeOffset? LastLoginAt,
    bool TwoFactorEnabled);

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
