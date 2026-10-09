// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VedettaVip.Api.Data;
using VedettaVip.Api.Security;
using VedettaVip.Api.Options;
using VedettaVip.Api.Services;
using VedettaVip.Shared.Contracts;
using MapNode = VedettaVip.Api.Data.Entities.MapNode;

namespace VedettaVip.Api.Endpoints;

/// <summary>
/// Endpoint dei nodi. Invariante: per ogni nodo di tipo Submap, Map[SubmapId].ParentMapId == MapId del nodo.
/// Ogni operazione che crea, sposta o elimina un nodo Submap aggiorna ParentMapId nello stesso
/// SaveChanges, quindi nella stessa transazione.
/// </summary>
public static class MapNodeEndpoints
{
    private const int MaxMapDepth = 64;

    public static IEndpointRouteBuilder MapMapNodeEndpoints(this IEndpointRouteBuilder app)
    {
        var nodes = app.MapGroup("/api/maps/{mapId:guid}/nodes").WithTags("Map nodes");

        nodes.MapGet("/{nodeId:guid}", GetNodeAsync);
        nodes.MapPost("/", CreateNodeAsync).RequireOperator();
        nodes.MapPut("/{nodeId:guid}", UpdateNodeAsync).RequireOperator();
        nodes.MapPut("/{nodeId:guid}/position", UpdatePositionAsync).RequireOperator();
        nodes.MapPost("/{nodeId:guid}/move", MoveNodeAsync).RequireOperator();
        nodes.MapDelete("/{nodeId:guid}", DeleteNodeAsync).RequireOperator();

        return app;
    }

    private static async Task<Results<Ok<MapNodeDto>, NotFound>> GetNodeAsync(
        Guid mapId, Guid nodeId, VedettaVipDbContext db, TimeProvider time, IOptions<AgentOptions> agentOptions, CancellationToken ct)
    {
        var node = await db.MapNodes
            .AsNoTracking()
            .Where(n => n.Id == nodeId && n.MapId == mapId)
            .Select(MapEndpoints.ToNodeDto(MapEndpoints.StaleBefore(time, agentOptions.Value)))
            .FirstOrDefaultAsync(ct);

        return node is null ? TypedResults.NotFound() : TypedResults.Ok(node);
    }

    private static async Task<Results<Created<MapNodeDto>, NotFound, ProblemHttpResult>> CreateNodeAsync(
        Guid mapId, CreateMapNodeDto dto, VedettaVipDbContext db, TimeProvider time, IOptions<AgentOptions> agentOptions,
        MapNotifier notifier, CancellationToken ct)
    {
        if (!await db.Maps.AnyAsync(m => m.Id == mapId, ct))
            return TypedResults.NotFound();

        var kind = dto.Kind!.Value;
        if (ValidateKind(kind, dto.DeviceId, dto.SubmapId) is { } kindError)
            return DbProblems.Problem(StatusCodes.Status400BadRequest, "Nodo incoerente", kindError);

        if (kind == MapNodeKind.Static && string.IsNullOrWhiteSpace(dto.LabelTemplate))
            return DbProblems.Problem(StatusCodes.Status400BadRequest, "Nodo incoerente", "Un nodo Static richiede LabelTemplate.");

        var node = new MapNode
        {
            Id = Guid.CreateVersion7(),
            MapId = mapId,
            Kind = kind,
            DeviceId = dto.DeviceId,
            SubmapId = dto.SubmapId,
            X = dto.X,
            Y = dto.Y,
            LabelTemplate = string.IsNullOrWhiteSpace(dto.LabelTemplate) ? DefaultLabel(kind) : dto.LabelTemplate,
            Icon = dto.Icon
        };

        switch (kind)
        {
            case MapNodeKind.Device:
                if (await CheckDeviceAsync(db, node.DeviceId!.Value, mapId, ct) is { } deviceProblem)
                    return deviceProblem;
                break;

            case MapNodeKind.Submap:
                if (await AttachSubmapAsync(db, node.SubmapId!.Value, mapId, node.Id, ct) is { } submapProblem)
                    return submapProblem;
                break;
        }

        db.MapNodes.Add(node);
        if (await db.TrySaveChangesAsync(ct) is { } saveProblem)
            return saveProblem;

        var created = await db.MapNodes.AsNoTracking()
            .Where(n => n.Id == node.Id)
            .Select(MapEndpoints.ToNodeDto(MapEndpoints.StaleBefore(time, agentOptions.Value)))
            .FirstAsync(ct);

        // La sottomappa agganciata cambia mappa padre (collegamento "torna alla mappa padre")
        await notifier.MapsChangedAsync(db, [mapId, .. Submap(node)]);
        return TypedResults.Created($"/api/maps/{mapId}/nodes/{node.Id}", created);
    }

    /// <summary>Modifica etichetta e icona. Tipo, riferimenti e mappa non cambiano (per la mappa: .../move).</summary>
    private static async Task<Results<Ok<MapNodeDto>, NotFound, ProblemHttpResult>> UpdateNodeAsync(
        Guid mapId, Guid nodeId, UpdateMapNodeDto dto, VedettaVipDbContext db, TimeProvider time, IOptions<AgentOptions> agentOptions,
        MapNotifier notifier, CancellationToken ct)
    {
        var node = await db.MapNodes.FirstOrDefaultAsync(n => n.Id == nodeId && n.MapId == mapId, ct);
        if (node is null)
            return TypedResults.NotFound();

        if (node.Kind == MapNodeKind.Static && string.IsNullOrWhiteSpace(dto.LabelTemplate))
            return DbProblems.Problem(StatusCodes.Status400BadRequest, "Nodo incoerente", "Un nodo Static richiede LabelTemplate.");

        node.LabelTemplate = string.IsNullOrWhiteSpace(dto.LabelTemplate) ? DefaultLabel(node.Kind) : dto.LabelTemplate;
        node.Icon = string.IsNullOrWhiteSpace(dto.Icon) ? null : dto.Icon.Trim();

        if (await db.TrySaveChangesAsync(ct) is { } saveProblem)
            return saveProblem;

        var updated = await db.MapNodes.AsNoTracking()
            .Where(n => n.Id == nodeId)
            .Select(MapEndpoints.ToNodeDto(MapEndpoints.StaleBefore(time, agentOptions.Value)))
            .FirstAsync(ct);

        await notifier.MapsChangedAsync(db, [mapId]);
        return TypedResults.Ok(updated);
    }

    private static async Task<Results<NoContent, NotFound>> UpdatePositionAsync(
        Guid mapId, Guid nodeId, NodePositionDto dto, VedettaVipDbContext db, MapNotifier notifier, CancellationToken ct)
    {
        var updated = await db.MapNodes
            .Where(n => n.Id == nodeId && n.MapId == mapId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.X, dto.X)
                .SetProperty(n => n.Y, dto.Y), ct);
        if (updated == 0)
            return TypedResults.NotFound();

        await notifier.MapsChangedAsync(db, [mapId]);
        return TypedResults.NoContent();
    }

    /// <summary>Sposta il nodo su un'altra mappa. I link del nodo appartengono alla mappa di origine e vengono eliminati.</summary>
    private static async Task<Results<Ok<MapNodeDto>, NotFound, ProblemHttpResult>> MoveNodeAsync(
        Guid mapId, Guid nodeId, MoveMapNodeDto dto, VedettaVipDbContext db, TimeProvider time, IOptions<AgentOptions> agentOptions,
        MapNotifier notifier, CancellationToken ct)
    {
        var node = await db.MapNodes.FirstOrDefaultAsync(n => n.Id == nodeId && n.MapId == mapId, ct);
        if (node is null)
            return TypedResults.NotFound();

        var targetMapId = dto.TargetMapId!.Value;
        if (targetMapId == mapId)
            return DbProblems.Problem(StatusCodes.Status400BadRequest, "Spostamento non valido",
                "La mappa di destinazione coincide con quella attuale: usare PUT .../position.");

        if (!await db.Maps.AnyAsync(m => m.Id == targetMapId, ct))
            return DbProblems.Problem(StatusCodes.Status400BadRequest, "Mappa inesistente", $"La mappa {targetMapId} non esiste.");

        switch (node.Kind)
        {
            case MapNodeKind.Device:
                if (await CheckDeviceAsync(db, node.DeviceId!.Value, targetMapId, ct) is { } deviceProblem)
                    return deviceProblem;
                break;

            case MapNodeKind.Submap:
                if (await AttachSubmapAsync(db, node.SubmapId!.Value, targetMapId, node.Id, ct) is { } submapProblem)
                    return submapProblem;
                break;
        }

        var links = await db.MapLinks
            .Where(l => l.FromNodeId == nodeId || l.ToNodeId == nodeId)
            .ToListAsync(ct);
        db.MapLinks.RemoveRange(links);

        node.MapId = targetMapId;
        node.X = dto.X;
        node.Y = dto.Y;

        if (await db.TrySaveChangesAsync(ct) is { } saveProblem)
            return saveProblem;

        var moved = await db.MapNodes.AsNoTracking()
            .Where(n => n.Id == nodeId)
            .Select(MapEndpoints.ToNodeDto(MapEndpoints.StaleBefore(time, agentOptions.Value)))
            .FirstAsync(ct);

        await notifier.MapsChangedAsync(db, [mapId, targetMapId, .. Submap(node)]);
        return TypedResults.Ok(moved);
    }

    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteNodeAsync(
        Guid mapId, Guid nodeId, VedettaVipDbContext db, MapNotifier notifier, CancellationToken ct)
    {
        var node = await db.MapNodes.FirstOrDefaultAsync(n => n.Id == nodeId && n.MapId == mapId, ct);
        if (node is null)
            return TypedResults.NotFound();

        // La sottomappa resta, ma diventa una mappa radice
        if (node.Kind == MapNodeKind.Submap
            && await db.Maps.FirstOrDefaultAsync(m => m.Id == node.SubmapId, ct) is { } submap
            && submap.ParentMapId == mapId)
        {
            submap.ParentMapId = null;
        }

        // I link collegati al nodo vengono eliminati dal CASCADE del database
        db.MapNodes.Remove(node);

        if (await db.TrySaveChangesAsync(ct) is { } saveProblem)
            return saveProblem;

        await notifier.MapsChangedAsync(db, [mapId, .. Submap(node)]);
        return TypedResults.NoContent();
    }

    private static IEnumerable<Guid> Submap(MapNode node) => node.SubmapId is { } id ? [id] : [];

    private static string? ValidateKind(MapNodeKind kind, Guid? deviceId, Guid? submapId) => kind switch
    {
        MapNodeKind.Device when deviceId is null || submapId is not null => "Un nodo Device richiede DeviceId e non ammette SubmapId.",
        MapNodeKind.Submap when submapId is null || deviceId is not null => "Un nodo Submap richiede SubmapId e non ammette DeviceId.",
        MapNodeKind.Static when deviceId is not null || submapId is not null => "Un nodo Static non ammette DeviceId né SubmapId.",
        _ => null
    };

    private static string DefaultLabel(MapNodeKind kind) => kind switch
    {
        MapNodeKind.Submap => "[Name]\n[Summary]",
        _ => MapNode.DefaultLabelTemplate
    };

    private static async Task<ProblemHttpResult?> CheckDeviceAsync(VedettaVipDbContext db, Guid deviceId, Guid mapId, CancellationToken ct)
    {
        if (!await db.Devices.AnyAsync(d => d.Id == deviceId, ct))
            return DbProblems.Problem(StatusCodes.Status400BadRequest, "Dispositivo inesistente", $"Il dispositivo {deviceId} non esiste.");

        if (await db.MapNodes.AnyAsync(n => n.MapId == mapId && n.DeviceId == deviceId, ct))
            return DbProblems.Problem(StatusCodes.Status409Conflict, "Dispositivo già presente", "Il dispositivo compare già su questa mappa.");

        return null;
    }

    /// <summary>
    /// Verifica che la sottomappa possa essere agganciata a <paramref name="parentMapId"/> e aggiorna
    /// il suo ParentMapId sull'entità tracciata (salvato insieme al nodo).
    /// </summary>
    private static async Task<ProblemHttpResult?> AttachSubmapAsync(
        VedettaVipDbContext db, Guid submapId, Guid parentMapId, Guid nodeId, CancellationToken ct)
    {
        var submap = await db.Maps.FirstOrDefaultAsync(m => m.Id == submapId, ct);
        if (submap is null)
            return DbProblems.Problem(StatusCodes.Status400BadRequest, "Sottomappa inesistente", $"La mappa {submapId} non esiste.");

        if (await db.MapNodes.AnyAsync(n => n.SubmapId == submapId && n.Id != nodeId, ct))
            return DbProblems.Problem(StatusCodes.Status409Conflict, "Sottomappa già collegata",
                "La sottomappa è già rappresentata da un nodo su un'altra mappa.");

        if (await IsSelfOrAncestorAsync(db, submapId, parentMapId, ct))
            return DbProblems.Problem(StatusCodes.Status409Conflict, "Ciclo tra mappe",
                "La mappa di destinazione è la sottomappa stessa o una sua discendente.");

        submap.ParentMapId = parentMapId;
        return null;
    }

    /// <summary>True se <paramref name="candidateId"/> è <paramref name="mapId"/> o uno dei suoi antenati.</summary>
    private static async Task<bool> IsSelfOrAncestorAsync(VedettaVipDbContext db, Guid candidateId, Guid mapId, CancellationToken ct)
    {
        Guid? current = mapId;
        for (var depth = 0; current is { } id; depth++)
        {
            if (id == candidateId || depth >= MaxMapDepth)
                return true; // oltre la profondità massima si assume un ciclo
            current = await db.Maps.Where(m => m.Id == id).Select(m => m.ParentMapId).FirstOrDefaultAsync(ct);
        }
        return false;
    }
}
