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
/// Storico dei down (interruzioni): elenco filtrabile per device o per tutti (vista globale), riepilogo di
/// disponibilità di un device, "Rimuovi risolte". Le interruzioni le aprono e chiudono i servizi (<see cref="OutageBook"/>).
/// </summary>
public static class OutageEndpoints
{
    private static readonly TimeSpan DefaultSummaryRange = TimeSpan.FromDays(30);
    private const int MaxLimit = 2000;

    public static IEndpointRouteBuilder MapOutageEndpoints(this IEndpointRouteBuilder app)
    {
        var outages = app.MapGroup("/api/outages").WithTags("Outages");
        outages.MapGet("/", GetOutagesAsync);
        outages.MapDelete("/resolved", DeleteResolvedAsync).RequireOperator();

        app.MapGet("/api/devices/{id:guid}/availability", GetSummaryAsync).WithTags("Outages");
        return app;
    }

    /// <summary>
    /// Interruzioni, le più recenti per prime. <paramref name="from"/>: solo quelle che toccano il periodo da lì in poi
    /// (le aperte sempre); <paramref name="before"/>: iniziate prima di quell'istante (pagina successiva). Senza deviceId
    /// è la vista globale, con il cliente come filtro.
    /// </summary>
    private static async Task<Ok<List<OutageDto>>> GetOutagesAsync(
        VedettaVipDbContext db, CancellationToken ct, Guid? deviceId = null, Guid? customerId = null,
        OutageStatusFilter status = OutageStatusFilter.All, OutageService? service = null,
        DateTimeOffset? from = null, DateTimeOffset? before = null, int limit = 500)
    {
        var query = Filter(db.Outages.AsNoTracking(), deviceId, status, service);
        if (customerId is { } cid) query = query.Where(o => o.Device!.CustomerId == cid);
        if (from is { } f)
        {
            var fromUtc = f.ToUniversalTime();
            query = query.Where(o => o.EndedAt == null || o.EndedAt >= fromUtc);
        }
        if (before is { } b)
        {
            var beforeUtc = b.ToUniversalTime();
            query = query.Where(o => o.StartedAt < beforeUtc);
        }

        var list = await query
            .OrderByDescending(o => o.StartedAt).ThenByDescending(o => o.Id)
            .Take(Math.Clamp(limit, 1, MaxLimit))
            .Select(o => new OutageDto(o.Id, o.DeviceId, o.Device!.Name, o.Device.Address,
                o.Device.Customer != null ? o.Device.Customer.Name : null,
                o.Service, o.Key, o.Detail, o.StartedAt, o.EndedAt, o.EndNote, o.Maintenance))
            .ToListAsync(ct);
        return TypedResults.Ok(list);
    }

    /// <summary>
    /// Riepilogo del device sul periodo (default ultimi 30 giorni): interruzioni che lo toccano, in corso, tempo giù al
    /// ping (fuori e dentro la manutenzione) e disponibilità.
    /// </summary>
    private static async Task<Results<Ok<OutageSummaryDto>, NotFound, ValidationProblem>> GetSummaryAsync(
        Guid id, VedettaVipDbContext db, TimeProvider time, CancellationToken ct, DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        var end = (to ?? time.GetUtcNow()).ToUniversalTime();
        var start = (from ?? end - DefaultSummaryRange).ToUniversalTime();
        if (start >= end)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["from"] = ["L'inizio deve precedere la fine."] });
        if (!await db.Devices.AnyAsync(d => d.Id == id, ct))
            return TypedResults.NotFound();

        var rows = await db.Outages.AsNoTracking()
            .Where(o => o.DeviceId == id && o.StartedAt < end && (o.EndedAt == null || o.EndedAt > start))
            .Select(o => new { o.Service, o.StartedAt, o.EndedAt, o.Maintenance })
            .ToListAsync(ct);

        var ping = rows.Where(r => r.Service == OutageService.Ping).ToList();
        var down = TimeSpan.Zero;
        var maintenanceDown = TimeSpan.Zero;
        foreach (var r in ping)
        {
            var overlap = OutageStats.Overlap(r.StartedAt, r.EndedAt, start, end);
            if (r.Maintenance is null) down += overlap;
            else maintenanceDown += overlap;
        }

        return TypedResults.Ok(new OutageSummaryDto(id, start, end, rows.Count, rows.Count(r => r.EndedAt == null), ping.Count,
            (long)down.TotalSeconds, (long)maintenanceDown.TotalSeconds, OutageStats.AvailabilityPct(down, start, end)));
    }

    /// <summary>"Rimuovi risolte": cancella le interruzioni finite del device (eventualmente di un solo servizio). Gli eventi restano.</summary>
    private static async Task<Results<Ok<OutageDeleteResultDto>, NotFound>> DeleteResolvedAsync(
        Guid deviceId, VedettaVipDbContext db, CancellationToken ct, OutageService? service = null)
    {
        if (!await db.Devices.AnyAsync(d => d.Id == deviceId, ct))
            return TypedResults.NotFound();
        var deleted = await Filter(db.Outages, deviceId, OutageStatusFilter.Resolved, service).ExecuteDeleteAsync(ct);
        return TypedResults.Ok(new OutageDeleteResultDto(deleted));
    }

    private static IQueryable<Outage> Filter(IQueryable<Outage> query, Guid? deviceId, OutageStatusFilter status, OutageService? service)
    {
        if (deviceId is { } id) query = query.Where(o => o.DeviceId == id);
        if (service is { } s) query = query.Where(o => o.Service == s);
        return status switch
        {
            OutageStatusFilter.Active => query.Where(o => o.EndedAt == null),
            OutageStatusFilter.Resolved => query.Where(o => o.EndedAt != null),
            _ => query
        };
    }
}
