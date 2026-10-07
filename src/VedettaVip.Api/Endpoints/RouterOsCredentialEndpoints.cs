// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using VedettaVip.Api.Data;
using VedettaVip.Api.Data.Entities;
using VedettaVip.Api.Security;
using VedettaVip.Api.Services;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Endpoints;

/// <summary>
/// Profili di accesso all'API RouterOS. Elenco per tutti (serve al pannello del device), scritture solo Admin.
/// Password write-only: cifrata al salvataggio, decifrata solo per gli agenti (<see cref="ResolveAsync"/>).
/// </summary>
public static class RouterOsCredentialEndpoints
{
    public static IEndpointRouteBuilder MapRouterOsCredentialEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/routeros-credentials").WithTags("RouterOsCredentials");
        group.MapGet("/", GetAllAsync);
        group.MapPost("/", CreateAsync).RequireAdmin();
        group.MapPut("/{id:guid}", UpdateAsync).RequireAdmin();
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAdmin();
        group.MapPut("/default", SetDefaultAsync).RequireAdmin();
        return app;
    }

    private static async Task<Ok<List<RouterOsCredentialDto>>> GetAllAsync(VedettaVipDbContext db, CancellationToken ct)
    {
        var defaultId = await DefaultIdAsync(db, ct);
        return TypedResults.Ok(await db.RouterOsCredentials.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new RouterOsCredentialDto(c.Id, c.Name, c.Description, c.Username, c.UseTls, c.Port, c.VerifyCertificate,
                c.Id == defaultId,
                db.Devices.Count(d => d.RouterOsCredentialId == c.Id),
                db.Customers.Count(x => x.RouterOsCredentialId == c.Id)))
            .ToListAsync(ct));
    }

    private static async Task<Results<Created<RouterOsCredentialDto>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        RouterOsCredentialUpsertDto dto, VedettaVipDbContext db, SecretProtector secrets, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(dto.Password))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(dto.Password)] = ["La password è obbligatoria."] });

        var credential = new RouterOsCredential
        {
            Id = Guid.CreateVersion7(),
            Name = dto.Name.Trim(),
            Username = dto.Username.Trim(),
            PasswordProtected = secrets.Protect(dto.Password)
        };
        Apply(credential, dto);
        db.RouterOsCredentials.Add(credential);
        if (await db.TrySaveChangesAsync(ct) is { } problem)
            return problem; // nome duplicato → 409
        return TypedResults.Created($"/api/routeros-credentials/{credential.Id}",
            new RouterOsCredentialDto(credential.Id, credential.Name, credential.Description, credential.Username, credential.UseTls,
                credential.Port, credential.VerifyCertificate, false, 0, 0));
    }

    /// <summary>Password vuota = invariata. Ogni modifica arriva subito agli agenti (utente, porta, TLS).</summary>
    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> UpdateAsync(
        Guid id, RouterOsCredentialUpsertDto dto, VedettaVipDbContext db, SecretProtector secrets, AgentNotifier agents, CancellationToken ct)
    {
        var credential = await db.RouterOsCredentials.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (credential is null)
            return TypedResults.NotFound();

        Apply(credential, dto);
        if (!string.IsNullOrEmpty(dto.Password))
            credential.PasswordProtected = secrets.Protect(dto.Password);

        if (await db.TrySaveChangesAsync(ct) is { } problem)
            return problem;
        await agents.TargetsChangedAsync();
        return TypedResults.NoContent();
    }

    private static void Apply(RouterOsCredential c, RouterOsCredentialUpsertDto dto)
    {
        c.Name = dto.Name.Trim();
        c.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        c.Username = dto.Username.Trim();
        c.UseTls = dto.UseTls;
        c.Port = dto.Port;
        c.VerifyCertificate = dto.UseTls && dto.VerifyCertificate;
    }

    /// <summary>409 se il profilo è usato da device, clienti o è il predefinito: va prima sostituito lì.</summary>
    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteAsync(Guid id, VedettaVipDbContext db, CancellationToken ct)
    {
        var credential = await db.RouterOsCredentials.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (credential is null)
            return TypedResults.NotFound();

        var devices = await db.Devices.CountAsync(d => d.RouterOsCredentialId == id, ct);
        var customers = await db.Customers.CountAsync(c => c.RouterOsCredentialId == id, ct);
        var isDefault = await DefaultIdAsync(db, ct) == id;
        if (devices > 0 || customers > 0 || isDefault)
        {
            var uses = new List<string>();
            if (devices > 0) uses.Add($"{devices} dispositivi");
            if (customers > 0) uses.Add($"{customers} clienti");
            if (isDefault) uses.Add("è il profilo predefinito");
            return DbProblems.Problem(StatusCodes.Status409Conflict, "Profilo RouterOS in uso",
                $"\"{credential.Name}\" è in uso ({string.Join(", ", uses)}): assegnare prima un altro profilo.");
        }

        db.RouterOsCredentials.Remove(credential);
        if (await db.TrySaveChangesAsync(ct) is { } problem)
            return problem;
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ValidationProblem>> SetDefaultAsync(
        RouterOsDefaultDto dto, VedettaVipDbContext db, AgentNotifier agents, CancellationToken ct)
    {
        if (dto.CredentialId is { } cid && !await db.RouterOsCredentials.AnyAsync(c => c.Id == cid, ct))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(dto.CredentialId)] = ["Il profilo non esiste."] });

        await db.MonitoringSettings.Where(s => s.Id == MonitoringSettings.SingletonId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.DefaultRouterOsCredentialId, dto.CredentialId), ct);
        await agents.TargetsChangedAsync();
        return TypedResults.NoContent();
    }

    private static Task<Guid?> DefaultIdAsync(VedettaVipDbContext db, CancellationToken ct) =>
        db.MonitoringSettings.AsNoTracking().Where(s => s.Id == MonitoringSettings.SingletonId)
            .Select(s => s.DefaultRouterOsCredentialId).FirstOrDefaultAsync(ct);

    /// <summary>
    /// Accesso RouterOS effettivo per device (profilo del device → del cliente → predefinito), con password decifrata.
    /// Null = nessun profilo o password non decifrabile (errore nel log): il device non viene letto.
    /// </summary>
    internal static async Task<Func<Guid?, Guid?, RouterOsTargetDto?>> ResolveAsync(VedettaVipDbContext db, SecretProtector secrets, CancellationToken ct)
    {
        var profiles = (await db.RouterOsCredentials.AsNoTracking().ToListAsync(ct))
            .ToDictionary(c => c.Id, c => secrets.Unprotect(c.PasswordProtected) is { } password
                ? new RouterOsTargetDto(c.Username, password, c.UseTls, c.Port, c.VerifyCertificate)
                : null);
        var byCustomer = await db.Customers.AsNoTracking().Where(c => c.RouterOsCredentialId != null)
            .ToDictionaryAsync(c => c.Id, c => c.RouterOsCredentialId!.Value, ct);
        var defaultId = await DefaultIdAsync(db, ct);

        return (deviceCredentialId, customerId) =>
        {
            var id = deviceCredentialId
                     ?? (customerId is { } c && byCustomer.TryGetValue(c, out var fromCustomer) ? fromCustomer : (Guid?)null)
                     ?? defaultId;
            return id is { } i ? profiles.GetValueOrDefault(i) : null;
        };
    }
}
