// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VedettaVip.Api.Data.Entities;
using VedettaVip.Api.Security;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Endpoints;

/// <summary>
/// Login con cookie (ASP.NET Core Identity), utente corrente, primo amministratore con codice di setup,
/// cambio password; gestione utenti (solo Admin).
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Auth");
        auth.MapGet("/me", GetMeAsync).AllowAnonymous();
        auth.MapPost("/login", LoginAsync).AllowAnonymous();
        auth.MapPost("/logout", LogoutAsync).AllowAnonymous();
        auth.MapPost("/setup", SetupAsync).AllowAnonymous();
        auth.MapPost("/change-password", ChangePasswordAsync);

        var users = app.MapGroup("/api/users").WithTags("Users").RequireAdmin();
        users.MapGet("/", GetUsersAsync);
        users.MapPost("/", CreateUserAsync);
        users.MapPut("/{id:guid}", UpdateUserAsync);
        users.MapPost("/{id:guid}/reset-password", ResetPasswordAsync);
        users.MapDelete("/{id:guid}", DeleteUserAsync);

        return app;
    }

    // ---------- Sessione ----------

    private static async Task<Ok<CurrentUserDto>> GetMeAsync(ClaimsPrincipal principal, UserManager<AppUser> users)
    {
        if (principal.Identity?.IsAuthenticated == true && await users.GetUserAsync(principal) is { Disabled: false } user)
        {
            var role = (await users.GetRolesAsync(user)).FirstOrDefault();
            return TypedResults.Ok(new CurrentUserDto(true, false, user.Id, user.UserName, user.DisplayName, role));
        }

        return TypedResults.Ok(new CurrentUserDto(false, !await users.Users.AnyAsync(), null, null, null, null));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> LoginAsync(
        LoginDto dto, UserManager<AppUser> users, SignInManager<AppUser> signIn, TimeProvider time, ILoggerFactory loggers)
    {
        var log = loggers.CreateLogger("VedettaVip.Auth");
        var user = await users.FindByNameAsync(dto.UserName.Trim());
        // Stesso messaggio per utente inesistente e password errata: non si rivela quali utenti esistono
        var invalid = DbProblems.Problem(StatusCodes.Status401Unauthorized, "Accesso negato", "Nome utente o password non validi.");
        if (user is null)
        {
            log.LogWarning("Login fallito per utente inesistente {UserName}", dto.UserName);
            return invalid;
        }
        if (user.Disabled)
        {
            log.LogWarning("Login rifiutato per l'utente disattivato {UserName}", user.UserName);
            return invalid;
        }

        var result = await signIn.PasswordSignInAsync(user, dto.Password, dto.RememberMe, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            log.LogWarning("Utente {UserName} bloccato per troppi tentativi", user.UserName);
            return DbProblems.Problem(StatusCodes.Status401Unauthorized, "Account bloccato",
                $"Troppi tentativi errati: riprovare dopo le {user.LockoutEnd?.ToLocalTime():HH:mm} o chiedere a un amministratore.");
        }
        if (!result.Succeeded)
        {
            log.LogWarning("Password errata per l'utente {UserName}", user.UserName);
            return invalid;
        }

        user.LastLoginAt = time.GetUtcNow();
        await users.UpdateAsync(user);
        log.LogInformation("Accesso di {UserName}", user.UserName);
        return TypedResults.NoContent();
    }

    private static async Task<NoContent> LogoutAsync(SignInManager<AppUser> signIn)
    {
        await signIn.SignOutAsync();
        return TypedResults.NoContent();
    }

    /// <summary>Primo amministratore: solo se non esiste nessun utente e con il codice scritto nel log dell'API.</summary>
    private static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult>> SetupAsync(
        SetupDto dto, UserManager<AppUser> users, SignInManager<AppUser> signIn, SetupCode setupCode, TimeProvider time, ILoggerFactory loggers)
    {
        if (await users.Users.AnyAsync())
            return DbProblems.Problem(StatusCodes.Status409Conflict, "Setup già eseguito", "Esiste già almeno un utente: accedere con le proprie credenziali.");
        if (!setupCode.Matches(dto.SetupCode))
        {
            loggers.CreateLogger("VedettaVip.Auth").LogWarning("Codice di setup errato");
            return DbProblems.Problem(StatusCodes.Status401Unauthorized, "Codice di setup errato",
                "Il codice è nel log dell'API (riga \"Codice di setup\"); cambia a ogni riavvio.");
        }

        var user = new AppUser { UserName = dto.UserName.Trim(), DisplayName = dto.DisplayName.Trim(), CreatedAt = time.GetUtcNow() };
        if (await CreateWithRoleAsync(users, user, dto.Password, Roles.Admin) is { } errors)
            return TypedResults.ValidationProblem(errors);

        await signIn.SignInAsync(user, isPersistent: false);
        loggers.CreateLogger("VedettaVip.Auth").LogInformation("Creato il primo amministratore {UserName}", user.UserName);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ValidationProblem, UnauthorizedHttpResult>> ChangePasswordAsync(
        ChangePasswordDto dto, ClaimsPrincipal principal, UserManager<AppUser> users, SignInManager<AppUser> signIn)
    {
        if (await users.GetUserAsync(principal) is not { } user)
            return TypedResults.Unauthorized();

        var result = await users.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);
        if (!result.Succeeded)
            return TypedResults.ValidationProblem(Errors(result, "NewPassword"));

        await signIn.RefreshSignInAsync(user); // nuovo security stamp: il cookie corrente resta valido
        return TypedResults.NoContent();
    }

    // ---------- Gestione utenti (Admin) ----------

    private static async Task<Ok<List<UserDto>>> GetUsersAsync(UserManager<AppUser> users)
    {
        var list = await users.Users.AsNoTracking().OrderBy(u => u.UserName).ToListAsync();
        var result = new List<UserDto>(list.Count);
        foreach (var u in list)
            result.Add(await ToDtoAsync(users, u));
        return TypedResults.Ok(result);
    }

    private static async Task<Results<Created<UserDto>, ValidationProblem>> CreateUserAsync(
        UserCreateDto dto, UserManager<AppUser> users, TimeProvider time)
    {
        if (!Roles.Seed.Any(r => r.Name == dto.Role))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["Role"] = ["Ruolo non valido."] });

        var user = new AppUser
        {
            UserName = dto.UserName.Trim(),
            DisplayName = dto.DisplayName.Trim(),
            Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim(),
            CreatedAt = time.GetUtcNow()
        };
        if (await CreateWithRoleAsync(users, user, dto.Password, dto.Role) is { } errors)
            return TypedResults.ValidationProblem(errors);

        return TypedResults.Created($"/api/users/{user.Id}", await ToDtoAsync(users, user));
    }

    /// <summary>Nome, email, ruolo, attivazione. Non si può togliere l'ultimo amministratore attivo, né disattivare sé stessi.</summary>
    private static async Task<Results<Ok<UserDto>, NotFound, ValidationProblem>> UpdateUserAsync(
        Guid id, UserUpdateDto dto, ClaimsPrincipal principal, UserManager<AppUser> users)
    {
        if (await users.FindByIdAsync(id.ToString()) is not { } user)
            return TypedResults.NotFound();
        if (!Roles.Seed.Any(r => r.Name == dto.Role))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["Role"] = ["Ruolo non valido."] });

        var self = users.GetUserId(principal) == id.ToString();
        if (self && dto.Disabled)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["Disabled"] = ["Non puoi disattivare il tuo utente."] });

        var currentRole = (await users.GetRolesAsync(user)).FirstOrDefault();
        var losesAdmin = currentRole == Roles.Admin && (dto.Role != Roles.Admin || dto.Disabled);
        if (losesAdmin && await ActiveAdminCountAsync(users) <= 1)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["Role"] = ["Deve restare almeno un amministratore attivo."] });

        user.DisplayName = dto.DisplayName.Trim();
        user.Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();
        var sessionsChange = user.Disabled != dto.Disabled || currentRole != dto.Role;
        user.Disabled = dto.Disabled;
        await users.UpdateAsync(user);

        if (currentRole != dto.Role)
        {
            if (currentRole is not null)
                await users.RemoveFromRoleAsync(user, currentRole);
            await users.AddToRoleAsync(user, dto.Role);
        }

        // Disattivazione o cambio ruolo: le sessioni aperte vengono rivalidate entro un minuto
        if (sessionsChange)
            await users.UpdateSecurityStampAsync(user);

        return TypedResults.Ok(await ToDtoAsync(users, user));
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> ResetPasswordAsync(
        Guid id, ResetPasswordDto dto, UserManager<AppUser> users)
    {
        if (await users.FindByIdAsync(id.ToString()) is not { } user)
            return TypedResults.NotFound();

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, token, dto.NewPassword);
        if (!result.Succeeded)
            return TypedResults.ValidationProblem(Errors(result, "NewPassword"));

        await users.SetLockoutEndDateAsync(user, null); // sblocca anche un account bloccato dai tentativi
        await users.ResetAccessFailedCountAsync(user);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> DeleteUserAsync(
        Guid id, ClaimsPrincipal principal, UserManager<AppUser> users)
    {
        if (await users.FindByIdAsync(id.ToString()) is not { } user)
            return TypedResults.NotFound();
        if (users.GetUserId(principal) == id.ToString())
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["Id"] = ["Non puoi eliminare il tuo utente."] });
        if ((await users.GetRolesAsync(user)).Contains(Roles.Admin) && !user.Disabled && await ActiveAdminCountAsync(users) <= 1)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["Id"] = ["Deve restare almeno un amministratore attivo."] });

        await users.DeleteAsync(user);
        return TypedResults.NoContent();
    }

    // ---------- Utilità ----------

    private static async Task<Dictionary<string, string[]>?> CreateWithRoleAsync(UserManager<AppUser> users, AppUser user, string password, string role)
    {
        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
            return Errors(created, "Password");
        await users.AddToRoleAsync(user, role);
        return null;
    }

    private static async Task<int> ActiveAdminCountAsync(UserManager<AppUser> users) =>
        (await users.GetUsersInRoleAsync(Roles.Admin)).Count(u => !u.Disabled);

    private static async Task<UserDto> ToDtoAsync(UserManager<AppUser> users, AppUser u) => new(
        u.Id, u.UserName!, u.DisplayName, u.Email, (await users.GetRolesAsync(u)).FirstOrDefault() ?? Roles.Viewer, u.Disabled,
        u.LockoutEnd is { } end && end > DateTimeOffset.UtcNow ? end : null, u.CreatedAt, u.LastLoginAt);

    /// <summary>Errori di Identity (policy password, nome duplicato) tradotti in italiano.</summary>
    private static Dictionary<string, string[]> Errors(IdentityResult result, string field) =>
        new() { [field] = result.Errors.Select(e => e.Code switch
        {
            "PasswordTooShort" => "La password deve avere almeno 10 caratteri.",
            "DuplicateUserName" => "Nome utente già in uso.",
            "InvalidUserName" => "Nome utente non valido: lettere, numeri e . _ - @",
            "PasswordMismatch" => "Password attuale errata.",
            "PasswordRequiresUniqueChars" => "La password deve contenere caratteri diversi.",
            _ => e.Description
        }).ToArray() };
}
