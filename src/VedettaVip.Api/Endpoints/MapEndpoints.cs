// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Linq.Expressions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VedettaVip.Api.Data;
using VedettaVip.Api.Security;
using VedettaVip.Api.Options;
using VedettaVip.Api.Services;
using Map = VedettaVip.Api.Data.Entities.Map;
using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;
using MapNode = VedettaVip.Api.Data.Entities.MapNode;

namespace VedettaVip.Api.Endpoints;

public static class MapEndpoints
{
    public static IEndpointRouteBuilder MapMapEndpoints(this IEndpointRouteBuilder app)
    {
        var maps = app.MapGroup("/api/maps").WithTags("Maps");

        maps.MapGet("/", GetMapsAsync);
        maps.MapGet("/{id:guid}", GetMapAsync);
        maps.MapPost("/", CreateMapAsync).RequireOperator();
        maps.MapPut("/{id:guid}", UpdateMapAsync).RequireOperator();
        maps.MapDelete("/{id:guid}", DeleteMapAsync).RequireOperator();

        return app;
    }

    /// <summary>
    /// Proiezione del nodo con lo stato corrente del dispositivo. Unknown se non c'è ancora uno stato,
    /// se il dispositivo è disabilitato o se l'ultimo report è più vecchio di <paramref name="staleBefore"/>
    /// (agente fermo: la mappa non deve restare verde). Lo stato dei nodi Submap lo calcola <see cref="WithSubmapStatusAsync"/>.
    /// </summary>
    internal static Expression<Func<MapNode, MapNodeDto>> ToNodeDto(DateTimeOffset staleBefore) => n => new MapNodeDto(
        n.Id,
        n.Kind,
        n.DeviceId,
        n.SubmapId,
        n.X,
        n.Y,
        n.LabelTemplate,
        n.Icon ?? (n.Device != null ? n.Device.Icon : null),
        n.Device != null ? n.Device.Name : n.Submap != null ? n.Submap.Name : null,
        n.Device != null ? n.Device.Address : null,
        n.Device != null ? (DeviceType?)n.Device.Type : null,
        n.Device != null && n.Device.Enabled && n.Device.Status != null && n.Device.Status.LastReportAt >= staleBefore
            ? n.Device.Status.State
            : NodeState.Unknown,
        null,
        null,
        n.Icon);

    internal static DateTimeOffset StaleBefore(TimeProvider time, AgentOptions options) =>
        time.GetUtcNow() - options.StaleAfter;

    private static async Task<Ok<List<MapSummaryDto>>> GetMapsAsync(VedettaVipDbContext db, CancellationToken ct)
    {
        var maps = await db.Maps
            .AsNoTracking()
            .OrderBy(m => m.Name)
            .Select(m => new MapSummaryDto(m.Id, m.Name, m.ParentMapId, m.Nodes.Count))
            .ToListAsync(ct);

        return TypedResults.Ok(maps);
    }

    private static async Task<Results<Ok<MapDto>, NotFound>> GetMapAsync(
        Guid id, VedettaVipDbContext db, TimeProvider time, IOptions<AgentOptions> agentOptions, ActiveMaintenance maintenance, CancellationToken ct)
    {
        var map = await db.Maps
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id, ct);
        if (map is null)
            return TypedResults.NotFound();

        var nodes = await db.MapNodes
            .AsNoTracking()
            .Where(n => n.MapId == id)
            .OrderBy(n => n.Id)
            .Select(ToNodeDto(StaleBefore(time, agentOptions.Value)))
            .ToListAsync(ct);

        // Nodi dei device in manutenzione (bordo tratteggiato sulla mappa)
        var inMaintenance = await maintenance.LoadAsync(db, ct);
        nodes = nodes.Select(n => n.DeviceId is { } d && inMaintenance(d) is { } w ? n with { Maintenance = w } : n).ToList();
        nodes = await WithSubmapStatusAsync(db, nodes, StaleBefore(time, agentOptions.Value), inMaintenance, ct);

        var links = await db.MapLinks
            .AsNoTracking()
            .Where(l => l.MapId == id)
            .OrderBy(l => l.Id)
            .Select(l => new MapLinkDto(l.Id, l.FromNodeId, l.ToNodeId, l.DeviceId, l.IfIndex, l.SpeedBps, l.UtilizationThresholdPct))
            .ToListAsync(ct);

        return TypedResults.Ok(new MapDto(map.Id, map.Name, map.ParentMapId, map.BackgroundImage, map.GridSize, nodes, links));
    }

    /// <summary>
    /// Stato aggregato dei nodi Submap: dispositivi della sottomappa e di tutte le sue discendenti (ognuno una volta),
    /// con lo stesso calcolo dello stato dei nodi device (disabilitato o report vecchio = Unknown). Regola in
    /// <see cref="SubmapStatus"/>; l'elenco dei membri permette al client di aggiornarlo dal vivo.
    /// </summary>
    private static async Task<List<MapNodeDto>> WithSubmapStatusAsync(
        VedettaVipDbContext db, List<MapNodeDto> nodes, DateTimeOffset staleBefore, Func<Guid, string?> inMaintenance, CancellationToken ct)
    {
        if (!nodes.Any(n => n.SubmapId is not null))
            return nodes;

        var children = (await db.Maps.AsNoTracking()
                .Where(m => m.ParentMapId != null)
                .Select(m => new { m.Id, Parent = m.ParentMapId!.Value })
                .ToListAsync(ct))
            .ToLookup(m => m.Parent, m => m.Id);
        var subtree = nodes.Where(n => n.SubmapId is not null)
            .ToDictionary(n => n.Id, n => Subtree(n.SubmapId!.Value, children));
        var mapIds = subtree.Values.SelectMany(ids => ids).Distinct().ToList();

        var members = (await db.MapNodes.AsNoTracking()
                .Where(n => mapIds.Contains(n.MapId) && n.DeviceId != null)
                .Select(n => new
                {
                    n.MapId,
                    DeviceId = n.DeviceId!.Value,
                    State = n.Device!.Enabled && n.Device.Status != null && n.Device.Status.LastReportAt >= staleBefore
                        ? n.Device.Status.State
                        : NodeState.Unknown
                })
                .ToListAsync(ct))
            .ToLookup(m => m.MapId);

        return nodes.Select(n =>
        {
            if (!subtree.TryGetValue(n.Id, out var ids))
                return n;
            var list = ids.SelectMany(id => members[id])
                .DistinctBy(m => m.DeviceId)
                .Select(m => new SubmapMemberDto(m.DeviceId, m.State, inMaintenance(m.DeviceId) is not null))
                .ToList();
            return n with { SubmapMembers = list, State = SubmapStatus.Aggregate(SubmapStatus.Count(list)) };
        }).ToList();
    }

    /// <summary>La mappa e tutte le discendenti (visita in ampiezza, protetta da cicli).</summary>
    private static List<Guid> Subtree(Guid root, ILookup<Guid, Guid> children)
    {
        var seen = new HashSet<Guid> { root };
        var queue = new Queue<Guid>([root]);
        while (queue.TryDequeue(out var id))
            foreach (var child in children[id])
                if (seen.Add(child))
                    queue.Enqueue(child);
        return [.. seen];
    }

    private static async Task<Results<Created<MapDto>, ValidationProblem, ProblemHttpResult>> CreateMapAsync(
        MapUpsertDto dto, VedettaVipDbContext db, CancellationToken ct)
    {
        if (Validate(dto) is { } errors)
            return TypedResults.ValidationProblem(errors);

        // Nasce come mappa radice: diventa sottomappa quando un nodo Submap la referenzia
        var map = new Map { Id = Guid.CreateVersion7(), Name = "" };
        Apply(map, dto);
        db.Maps.Add(map);

        if (await db.TrySaveChangesAsync(ct) is { } saveProblem)
            return saveProblem;

        return TypedResults.Created($"/api/maps/{map.Id}",
            new MapDto(map.Id, map.Name, map.ParentMapId, map.BackgroundImage, map.GridSize, [], []));
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem, ProblemHttpResult>> UpdateMapAsync(
        Guid id, MapUpsertDto dto, VedettaVipDbContext db, CancellationToken ct)
    {
        var map = await db.Maps.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (map is null)
            return TypedResults.NotFound();

        if (Validate(dto) is { } errors)
            return TypedResults.ValidationProblem(errors);

        Apply(map, dto);

        if (await db.TrySaveChangesAsync(ct) is { } saveProblem)
            return saveProblem;

        return TypedResults.NoContent();
    }

    /// <summary>
    /// Elimina la mappa con i suoi nodi e link (CASCADE). Una mappa ancora rappresentata da un nodo Submap
    /// non si elimina (409): va prima rimosso il nodo. Le sue sottomappe restano e diventano radici,
    /// nello stesso SaveChanges (i loro nodi Submap stanno su questa mappa e cadono in CASCADE).
    /// </summary>
    private static async Task<Results<NoContent, NotFound, ProblemHttpResult>> DeleteMapAsync(
        Guid id, VedettaVipDbContext db, CancellationToken ct)
    {
        var map = await db.Maps.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (map is null)
            return TypedResults.NotFound();

        var parent = await db.MapNodes
            .AsNoTracking()
            .Where(n => n.SubmapId == id)
            .Select(n => new MapRefDto(n.MapId, n.Map!.Name))
            .FirstOrDefaultAsync(ct);
        if (parent is not null)
            return DbProblems.Problem(StatusCodes.Status409Conflict, "Sottomappa in uso",
                $"La mappa {map.Name} è una sottomappa di {parent.Name} ({parent.Id}): eliminare prima il nodo che la rappresenta.");

        var children = await db.Maps.Where(m => m.ParentMapId == id).ToListAsync(ct);
        foreach (var child in children)
            child.ParentMapId = null;

        db.Maps.Remove(map);

        if (await db.TrySaveChangesAsync(ct) is { } saveProblem)
            return saveProblem;

        return TypedResults.NoContent();
    }

    private static void Apply(Map map, MapUpsertDto dto)
    {
        map.Name = dto.Name.Trim();
        map.BackgroundImage = string.IsNullOrWhiteSpace(dto.BackgroundImage) ? null : dto.BackgroundImage.Trim();
        map.GridSize = dto.GridSize;
    }

    private static Dictionary<string, string[]>? Validate(MapUpsertDto dto) =>
        string.IsNullOrWhiteSpace(dto.Name)
            ? new() { [nameof(dto.Name)] = ["Il nome è obbligatorio."] }
            : null;
}
