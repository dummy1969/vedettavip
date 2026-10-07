// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using VedettaVip.Api.Data;
using VedettaVip.Api.Data.Entities;
using VedettaVip.Api.Security;
using VedettaVip.Api.Services;
using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;

namespace VedettaVip.Api.Endpoints;

/// <summary>Interfacce e peer WireGuard di un router (ultima lettura) e scelta di quelli da sorvegliare.</summary>
public static class RouterOsWatchEndpoints
{
    public static IEndpointRouteBuilder MapRouterOsWatchEndpoints(this IEndpointRouteBuilder app)
    {
        var devices = app.MapGroup("/api/devices").WithTags("RouterOs");
        devices.MapGet("/{id:guid}/routeros", GetDetailAsync);
        devices.MapPut("/{id:guid}/routeros/watches", ToggleAsync).RequireOperator();
        return app;
    }

    private static async Task<Results<Ok<DeviceRouterOsDetailDto>, NotFound>> GetDetailAsync(
        Guid id, VedettaVipDbContext db, RouterOsCache cache, CancellationToken ct)
    {
        if (!await db.Devices.AnyAsync(d => d.Id == id, ct))
            return TypedResults.NotFound();
        return TypedResults.Ok(new DeviceRouterOsDetailDto(id, cache.Latest(id), await WatchesAsync(db, id, ct)));
    }

    /// <summary>
    /// Attiva o toglie la sorveglianza. Togliendola, l'eventuale avviso aperto viene preso in carico e chiuso (senza
    /// notifica di rientro: è una scelta di chi gestisce, non un ripristino). Lo stato si valuta alla prossima lettura.
    /// </summary>
    private static async Task<Results<Ok<List<RouterOsWatchDto>>, NotFound>> ToggleAsync(
        Guid id, RouterOsWatchToggleDto dto, VedettaVipDbContext db, RouterOsWatchService service, CancellationToken ct)
    {
        if (!await db.Devices.AnyAsync(d => d.Id == id, ct))
            return TypedResults.NotFound();

        var kind = (RouterOsWatchKind)dto.Kind;
        var existing = await db.RouterOsWatches.FirstOrDefaultAsync(w => w.DeviceId == id && w.Kind == kind && w.Key == dto.Key, ct);
        if (dto.Watched && existing is null)
        {
            db.RouterOsWatches.Add(new RouterOsWatch
            {
                Id = Guid.CreateVersion7(), DeviceId = id, Kind = kind, Key = dto.Key.Trim(), Label = dto.Label.Trim(),
                State = RouterOsWatchState.Unknown
            });
            await db.SaveChangesAsync(ct);
        }
        else if (!dto.Watched && existing is not null)
        {
            db.RouterOsWatches.Remove(existing);
            await db.SaveChangesAsync(ct);
            await service.CloseOpenAlertAsync(id, RouterOsWatchRules.AlertKey(kind, existing.Key), ct);
        }
        return TypedResults.Ok(await WatchesAsync(db, id, ct));
    }

    private static async Task<List<RouterOsWatchDto>> WatchesAsync(VedettaVipDbContext db, Guid deviceId, CancellationToken ct) =>
        (await db.RouterOsWatches.AsNoTracking().Where(w => w.DeviceId == deviceId).OrderBy(w => w.Kind).ThenBy(w => w.Label).ToListAsync(ct))
        .Select(w => new RouterOsWatchDto((RouterOsWatchKindDto)w.Kind, w.Key, w.Label, w.State switch
        {
            RouterOsWatchState.Up => NodeState.Up,
            RouterOsWatchState.Down => NodeState.Down,
            _ => NodeState.Unknown
        }, w.Since, w.Detail))
        .ToList();
}
