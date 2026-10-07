// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using VedettaVip.Api.Data;
using VedettaVip.Api.Security;
using VedettaVip.Api.Services;
using VedettaVip.Shared.Contracts;
using MapLink = VedettaVip.Api.Data.Entities.MapLink;

namespace VedettaVip.Api.Endpoints;

/// <summary>
/// Endpoint dei link. Un link appartiene a una mappa e collega due nodi diversi della stessa mappa;
/// DeviceId + IfIndex indicano l'interfaccia da cui leggere il traffico (tx = From → To).
/// Sono ammessi più link tra la stessa coppia di nodi (uplink ridondanti, membri di un bond).
/// </summary>
public static class MapLinkEndpoints
{
    public static IEndpointRouteBuilder MapMapLinkEndpoints(this IEndpointRouteBuilder app)
    {
        var links = app.MapGroup("/api/maps/{mapId:guid}/links").WithTags("Map links");

        links.MapGet("/{linkId:guid}", GetLinkAsync);
        links.MapPost("/", CreateLinkAsync).RequireOperator();
        links.MapPut("/{linkId:guid}", UpdateLinkAsync).RequireOperator();
        links.MapDelete("/{linkId:guid}", DeleteLinkAsync).RequireOperator();

        return app;
    }

    private static async Task<Results<Ok<MapLinkDto>, NotFound>> GetLinkAsync(
        Guid mapId, Guid linkId, VedettaVipDbContext db, CancellationToken ct)
    {
        var link = await db.MapLinks
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == linkId && l.MapId == mapId, ct);

        return link is null ? TypedResults.NotFound() : TypedResults.Ok(ToDto(link));
    }

    private static async Task<Results<Created<MapLinkDto>, NotFound, ValidationProblem, ProblemHttpResult>> CreateLinkAsync(
        Guid mapId, MapLinkUpsertDto dto, VedettaVipDbContext db, AgentNotifier agents, CancellationToken ct)
    {
        if (!await db.Maps.AnyAsync(m => m.Id == mapId, ct))
            return TypedResults.NotFound();

        if (await ValidateAsync(db, mapId, dto, ct) is { } errors)
            return TypedResults.ValidationProblem(errors);

        var link = new MapLink { Id = Guid.CreateVersion7(), MapId = mapId };
        Apply(link, dto);
        db.MapLinks.Add(link);

        if (await db.TrySaveChangesAsync(ct) is { } saveProblem)
            return saveProblem;

        if (link.IfIndex is not null)
            await agents.TargetsChangedAsync();
        return TypedResults.Created($"/api/maps/{mapId}/links/{link.Id}", ToDto(link));
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem, ProblemHttpResult>> UpdateLinkAsync(
        Guid mapId, Guid linkId, MapLinkUpsertDto dto, VedettaVipDbContext db, AgentNotifier agents, CancellationToken ct)
    {
        var link = await db.MapLinks.FirstOrDefaultAsync(l => l.Id == linkId && l.MapId == mapId, ct);
        if (link is null)
            return TypedResults.NotFound();

        if (await ValidateAsync(db, mapId, dto, ct) is { } errors)
            return TypedResults.ValidationProblem(errors);

        var interfaceChanged = link.DeviceId != dto.DeviceId || link.IfIndex != dto.IfIndex;
        Apply(link, dto);

        if (await db.TrySaveChangesAsync(ct) is { } saveProblem)
            return saveProblem;

        if (interfaceChanged)
            await agents.TargetsChangedAsync();
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, NotFound>> DeleteLinkAsync(
        Guid mapId, Guid linkId, VedettaVipDbContext db, CancellationToken ct)
    {
        var deleted = await db.MapLinks
            .Where(l => l.Id == linkId && l.MapId == mapId)
            .ExecuteDeleteAsync(ct);

        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static MapLinkDto ToDto(MapLink l) => new(l.Id, l.FromNodeId, l.ToNodeId, l.DeviceId, l.IfIndex, l.SpeedBps, l.UtilizationThresholdPct);

    private static void Apply(MapLink link, MapLinkUpsertDto dto)
    {
        link.FromNodeId = dto.FromNodeId!.Value;
        link.ToNodeId = dto.ToNodeId!.Value;
        link.DeviceId = dto.DeviceId;
        link.IfIndex = dto.IfIndex;
        link.SpeedBps = dto.SpeedBps;
        link.UtilizationThresholdPct = dto.UtilizationThresholdPct;
    }

    private static async Task<Dictionary<string, string[]>?> ValidateAsync(
        VedettaVipDbContext db, Guid mapId, MapLinkUpsertDto dto, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var from = dto.FromNodeId!.Value;
        var to = dto.ToNodeId!.Value;

        if (from == to)
        {
            errors[nameof(dto.ToNodeId)] = ["Un link deve collegare due nodi diversi."];
        }
        else
        {
            var found = await db.MapNodes
                .Where(n => n.MapId == mapId && (n.Id == from || n.Id == to))
                .Select(n => n.Id)
                .ToListAsync(ct);

            if (!found.Contains(from))
                errors[nameof(dto.FromNodeId)] = ["Il nodo di partenza non esiste su questa mappa."];
            if (!found.Contains(to))
                errors[nameof(dto.ToNodeId)] = ["Il nodo di arrivo non esiste su questa mappa."];
        }

        if (dto.DeviceId is { } deviceId)
        {
            if (!await db.Devices.AnyAsync(d => d.Id == deviceId, ct))
                errors[nameof(dto.DeviceId)] = ["Il dispositivo sorgente del traffico non esiste."];
        }
        else if (dto.IfIndex is not null)
        {
            errors[nameof(dto.IfIndex)] = ["IfIndex richiede DeviceId."];
        }

        return errors.Count > 0 ? errors : null;
    }
}
