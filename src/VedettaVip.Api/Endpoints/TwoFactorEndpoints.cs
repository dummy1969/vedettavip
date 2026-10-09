// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using VedettaVip.Api.Data.Entities;
using VedettaVip.Api.Security;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Endpoints;

/// <summary>
/// Verifica in due passaggi dell'utente corrente, facoltativa: codici TOTP (RFC 6238) di un'app di autenticazione,
/// con il provider Authenticator di Identity. Chiave e codici di recupero sono cifrati da <see cref="ProtectedUserStore"/>.
/// Le operazioni che cambiano il security stamp rinnovano il cookie della sessione corrente (RefreshSignInAsync),
/// altrimenti verrebbe invalidato entro un minuto; le altre sessioni dell'utente cadono.
/// </summary>
public static class TwoFactorEndpoints
{
    public static IEndpointRouteBuilder MapTwoFactorEndpoints(this IEndpointRouteBuilder app)
    {
        var tfa = app.MapGroup("/api/auth/2fa").WithTags("Auth");
        tfa.MapGet("/", GetStatusAsync);
        tfa.MapPost("/setup", SetupAsync);
        tfa.MapPost("/enable", EnableAsync);
        tfa.MapPost("/recovery-codes", RegenerateRecoveryCodesAsync);
        tfa.MapPost("/disable", DisableAsync);
        tfa.MapPost("/forget-browser", ForgetBrowserAsync);
        return app;
    }

    private static async Task<Results<Ok<TwoFactorStatusDto>, UnauthorizedHttpResult>> GetStatusAsync(
        ClaimsPrincipal principal, UserManager<AppUser> users, SignInManager<AppUser> signIn)
    {
        if (await users.GetUserAsync(principal) is not { } user)
            return TypedResults.Unauthorized();

        var enabled = await users.GetTwoFactorEnabledAsync(user);
        return TypedResults.Ok(new TwoFactorStatusDto(
            enabled,
            enabled ? await users.CountRecoveryCodesAsync(user) : 0,
            enabled && await signIn.IsTwoFactorClientRememberedAsync(user)));
    }

    /// <summary>Nuova chiave da registrare nell'app (sostituisce una configurazione iniziata e non confermata).</summary>
    private static async Task<Results<Ok<TwoFactorSetupDto>, UnauthorizedHttpResult, ProblemHttpResult>> SetupAsync(
        ClaimsPrincipal principal, UserManager<AppUser> users, SignInManager<AppUser> signIn)
    {
        if (await users.GetUserAsync(principal) is not { } user)
            return TypedResults.Unauthorized();
        if (await users.GetTwoFactorEnabledAsync(user))
            return DbProblems.Problem(StatusCodes.Status409Conflict, "Verifica in due passaggi già attiva",
                "Per registrare un altro telefono disattivarla e riattivarla.");

        await users.ResetAuthenticatorKeyAsync(user);
        await signIn.RefreshSignInAsync(user);
        var key = await users.GetAuthenticatorKeyAsync(user)
                  ?? throw new InvalidOperationException("Chiave dell'app di autenticazione non salvata.");
        var uri = TwoFactorText.AuthenticatorUri(user.UserName!, key);
        return TypedResults.Ok(new TwoFactorSetupDto(TwoFactorText.FormatKey(key), uri, TwoFactorText.QrCodeDataUri(uri)));
    }

    /// <summary>Attiva la verifica dopo il primo codice giusto e restituisce i codici di recupero (mostrati una volta sola).</summary>
    private static async Task<Results<Ok<RecoveryCodesDto>, UnauthorizedHttpResult, ValidationProblem, ProblemHttpResult>> EnableAsync(
        TwoFactorCodeDto dto, ClaimsPrincipal principal, UserManager<AppUser> users, SignInManager<AppUser> signIn, ILoggerFactory loggers)
    {
        if (await users.GetUserAsync(principal) is not { } user)
            return TypedResults.Unauthorized();
        if (await users.GetTwoFactorEnabledAsync(user))
            return DbProblems.Problem(StatusCodes.Status409Conflict, "Verifica in due passaggi già attiva", null);
        if (string.IsNullOrEmpty(await users.GetAuthenticatorKeyAsync(user)))
            return DbProblems.Problem(StatusCodes.Status400BadRequest, "Configurazione non iniziata", "Generare prima il QR code.");
        if (!await VerifyCodeAsync(users, user, dto.Code))
            return InvalidCode();

        await users.SetTwoFactorEnabledAsync(user, true);
        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, TwoFactorDefaults.RecoveryCodeCount);
        await signIn.RefreshSignInAsync(user);
        loggers.CreateLogger("VedettaVip.Auth").LogInformation("Verifica in due passaggi attivata da {UserName}", user.UserName);
        return TypedResults.Ok(new RecoveryCodesDto([.. codes ?? []]));
    }

    /// <summary>Nuovi codici di recupero (i vecchi non valgono più); richiede il codice attuale dell'app.</summary>
    private static async Task<Results<Ok<RecoveryCodesDto>, UnauthorizedHttpResult, ValidationProblem, ProblemHttpResult>> RegenerateRecoveryCodesAsync(
        TwoFactorCodeDto dto, ClaimsPrincipal principal, UserManager<AppUser> users, ILoggerFactory loggers)
    {
        if (await users.GetUserAsync(principal) is not { } user)
            return TypedResults.Unauthorized();
        if (!await users.GetTwoFactorEnabledAsync(user))
            return DbProblems.Problem(StatusCodes.Status400BadRequest, "Verifica in due passaggi non attiva", null);
        if (!await VerifyCodeAsync(users, user, dto.Code))
            return InvalidCode();

        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, TwoFactorDefaults.RecoveryCodeCount);
        loggers.CreateLogger("VedettaVip.Auth").LogInformation("Nuovi codici di recupero per {UserName}", user.UserName);
        return TypedResults.Ok(new RecoveryCodesDto([.. codes ?? []]));
    }

    /// <summary>Disattiva con la password (funziona anche se il telefono è perso, finché la sessione è aperta).</summary>
    private static async Task<Results<NoContent, UnauthorizedHttpResult, ValidationProblem>> DisableAsync(
        TwoFactorDisableDto dto, ClaimsPrincipal principal, UserManager<AppUser> users, SignInManager<AppUser> signIn, ILoggerFactory loggers)
    {
        if (await users.GetUserAsync(principal) is not { } user)
            return TypedResults.Unauthorized();
        if (!await users.CheckPasswordAsync(user, dto.Password))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["Password"] = ["Password errata."] });

        await TurnOffAsync(users, user);
        await signIn.ForgetTwoFactorClientAsync();
        await signIn.RefreshSignInAsync(user);
        loggers.CreateLogger("VedettaVip.Auth").LogWarning("Verifica in due passaggi disattivata da {UserName}", user.UserName);
        return TypedResults.NoContent();
    }

    /// <summary>Questo browser chiederà di nuovo il codice al prossimo accesso.</summary>
    private static async Task<NoContent> ForgetBrowserAsync(SignInManager<AppUser> signIn)
    {
        await signIn.ForgetTwoFactorClientAsync();
        return TypedResults.NoContent();
    }

    /// <summary>Spegne la verifica e cancella chiave e codici di recupero; cambia il security stamp (sessioni e browser ricordati).</summary>
    internal static async Task TurnOffAsync(UserManager<AppUser> users, AppUser user)
    {
        await users.SetTwoFactorEnabledAsync(user, false);
        await users.RemoveAuthenticationTokenAsync(user, ProtectedUserStore.InternalLoginProvider, ProtectedUserStore.RecoveryCodesTokenName);
        await users.ResetAuthenticatorKeyAsync(user);
    }

    private static Task<bool> VerifyCodeAsync(UserManager<AppUser> users, AppUser user, string code) =>
        users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, TwoFactorText.NormalizeCode(code));

    private static ValidationProblem InvalidCode() =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>
            { ["Code"] = ["Codice non valido: usare quello attuale dell'app e controllare che l'ora del telefono sia esatta."] });
}
