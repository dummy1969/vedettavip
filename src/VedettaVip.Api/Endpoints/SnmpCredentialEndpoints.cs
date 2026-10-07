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
/// Profili di credenziali SNMP. Elenco per tutti (serve al pannello del device), scritture solo Admin.
/// La community è write-only: cifrata al salvataggio, decifrata solo per gli agenti (<see cref="ResolveCommunitiesAsync"/>).
/// </summary>
public static class SnmpCredentialEndpoints
{
    public static IEndpointRouteBuilder MapSnmpCredentialEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/snmp-credentials").WithTags("SnmpCredentials");
        group.MapGet("/", GetAllAsync);
        group.MapPost("/", CreateAsync).RequireAdmin();
        group.MapPut("/{id:guid}", UpdateAsync).RequireAdmin();
        group.MapDelete("/{id:guid}", DeleteAsync).RequireAdmin();
        group.MapPut("/default", SetDefaultAsync).RequireAdmin();
        return app;
    }

    private static async Task<Ok<List<SnmpCredentialDto>>> GetAllAsync(VedettaVipDbContext db, CancellationToken ct)
    {
        var defaultId = await DefaultIdAsync(db, ct);
        return TypedResults.Ok(await db.SnmpCredentials.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new SnmpCredentialDto(c.Id, c.Name, c.Description, c.Id == defaultId,
                db.Devices.Count(d => d.SnmpCredentialId == c.Id),
                db.Customers.Count(x => x.SnmpCredentialId == c.Id)))
            .ToListAsync(ct));
    }

    private static async Task<Results<Created<SnmpCredentialDto>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        SnmpCredentialUpsertDto dto, VedettaVipDbContext db, SecretProtector secrets, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(dto.Community))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(dto.Community)] = ["La community è obbligatoria."] });

        var credential = new SnmpCredential
        {
            Id = Guid.CreateVersion7(),
            Name = dto.Name.Trim(),
            Description = Blank(dto.Description),
            CommunityProtected = secrets.Protect(dto.Community)
        };
        db.SnmpCredentials.Add(credential);
        if (await db.TrySaveChangesAsync(ct) is { } problem)
            return problem; // nome duplicato → 409
        return TypedResults.Created($"/api/snmp-credentials/{credential.Id}",
            new SnmpCredentialDto(credential.Id, credential.Name, credential.Description, false, 0, 0));
    }

    /// <summary>Community vuota = invariata. Se cambia, gli agenti rileggono subito i target.</summary>
    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> UpdateAsync(
        Guid id, SnmpCredentialUpsertDto dto, VedettaVipDbContext db, SecretProtector secrets, AgentNotifier agents, CancellationToken ct)
    {
        var credential = await db.SnmpCredentials.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (credential is null)
            return TypedResults.NotFound();

        credential.Name = dto.Name.Trim();
        credential.Description = Blank(dto.Description);
        var communityChanged = !string.IsNullOrEmpty(dto.Community);
        if (communityChanged)
            credential.CommunityProtected = secrets.Protect(dto.Community!);

        if (await db.TrySaveChangesAsync(ct) is { } problem)
            return problem;
        if (communityChanged)
            await agents.TargetsChangedAsync();
        return TypedResults.NoContent();
    }

    /// <summary>409 se il profilo è usato da device, clienti o è il predefinito: va prima sostituito lì.</summary>
    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteAsync(Guid id, VedettaVipDbContext db, CancellationToken ct)
    {
        var credential = await db.SnmpCredentials.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (credential is null)
            return TypedResults.NotFound();

        var devices = await db.Devices.CountAsync(d => d.SnmpCredentialId == id, ct);
        var customers = await db.Customers.CountAsync(c => c.SnmpCredentialId == id, ct);
        var isDefault = await DefaultIdAsync(db, ct) == id;
        if (devices > 0 || customers > 0 || isDefault)
        {
            var uses = new List<string>();
            if (devices > 0) uses.Add($"{devices} dispositivi");
            if (customers > 0) uses.Add($"{customers} clienti");
            if (isDefault) uses.Add("è il profilo predefinito");
            return DbProblems.Problem(StatusCodes.Status409Conflict, "Profilo SNMP in uso",
                $"\"{credential.Name}\" è in uso ({string.Join(", ", uses)}): assegnare prima un altro profilo.");
        }

        db.SnmpCredentials.Remove(credential);
        if (await db.TrySaveChangesAsync(ct) is { } problem)
            return problem; // assegnato nel frattempo → 409
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ValidationProblem>> SetDefaultAsync(
        SnmpDefaultDto dto, VedettaVipDbContext db, AgentNotifier agents, CancellationToken ct)
    {
        if (dto.CredentialId is { } cid && !await db.SnmpCredentials.AnyAsync(c => c.Id == cid, ct))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(dto.CredentialId)] = ["Il profilo non esiste."] });

        await db.MonitoringSettings.Where(s => s.Id == MonitoringSettings.SingletonId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.DefaultSnmpCredentialId, dto.CredentialId), ct);
        await agents.TargetsChangedAsync();
        return TypedResults.NoContent();
    }

    private static Task<Guid?> DefaultIdAsync(VedettaVipDbContext db, CancellationToken ct) =>
        db.MonitoringSettings.AsNoTracking().Where(s => s.Id == MonitoringSettings.SingletonId)
            .Select(s => s.DefaultSnmpCredentialId).FirstOrDefaultAsync(ct);

    /// <summary>
    /// Community effettiva per device (profilo del device → del cliente → predefinito), decifrata per gli agenti.
    /// Assente = l'agente usa la propria configurazione; anche un segreto non decifrabile ricade lì (errore nel log).
    /// </summary>
    internal static async Task<Func<Guid?, Guid?, string?>> ResolveCommunitiesAsync(VedettaVipDbContext db, SecretProtector secrets, CancellationToken ct)
    {
        var communities = (await db.SnmpCredentials.AsNoTracking().Select(c => new { c.Id, c.CommunityProtected }).ToListAsync(ct))
            .ToDictionary(c => c.Id, c => secrets.Unprotect(c.CommunityProtected));
        var byCustomer = await db.Customers.AsNoTracking().Where(c => c.SnmpCredentialId != null)
            .ToDictionaryAsync(c => c.Id, c => c.SnmpCredentialId!.Value, ct);
        var defaultId = await DefaultIdAsync(db, ct);

        return (deviceCredentialId, customerId) =>
        {
            var id = deviceCredentialId
                     ?? (customerId is { } c && byCustomer.TryGetValue(c, out var fromCustomer) ? fromCustomer : (Guid?)null)
                     ?? defaultId;
            return id is { } i ? communities.GetValueOrDefault(i) : null;
        };
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
