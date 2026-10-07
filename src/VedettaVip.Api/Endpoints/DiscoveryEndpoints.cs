// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using VedettaVip.Api.Data;
using VedettaVip.Api.Data.Entities;
using VedettaVip.Api.Security;
using VedettaVip.Api.Services;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Endpoints;

/// <summary>
/// Discovery: vicini letti dagli agenti (/ip/neighbor, LLDP, CDP) → proposte di link e di dispositivi da confermare
/// (<see cref="DiscoveryPlanner"/>). Niente viene aggiunto da solo: l'operatore sceglie e conferma.
/// </summary>
public static class DiscoveryEndpoints
{
    public static IEndpointRouteBuilder MapDiscoveryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/discovery").WithTags("Discovery");
        group.MapGet("/", GetAsync);
        group.MapPost("/accept", AcceptAsync).RequireOperator();
        group.MapPost("/ignore", IgnoreAsync).RequireOperator();
        group.MapPost("/forget", ForgetAsync).RequireOperator();
        group.MapPost("/refresh", async (AgentNotifier agents) =>
        {
            await agents.NeighborsRequestedAsync();
            return TypedResults.Accepted("/api/discovery");
        }).RequireOperator();
        group.MapGet("/scans", GetScansAsync);
        group.MapPost("/scans", CreateScanAsync).RequireOperator();
        group.MapDelete("/scans/{id:guid}", DeleteScanAsync).RequireOperator();
        return app;
    }

    /// <summary>Endpoint degli agenti per le scansioni (gruppo /api/agent, protetto da X-Agent-Key).</summary>
    internal static void MapAgentScanEndpoints(RouteGroupBuilder agent)
    {
        agent.MapGet("/scans/{id:guid}", ClaimScanAsync);
        agent.MapPost("/scans/{id:guid}", PostScanResultAsync);
    }

    // ---------- Scansioni ----------

    private static DiscoveryScanDto ToDto(DiscoveryScan s, int alive) =>
        new(s.Id, s.Cidr, s.CustomerId, s.MapId, s.Status, s.CreatedAt, s.CompletedAt, s.Scanned, s.Total, alive, s.Error, s.CreatedBy);

    private static async Task<Ok<List<DiscoveryScanDto>>> GetScansAsync(VedettaVipDbContext db, CancellationToken ct) =>
        TypedResults.Ok((await db.DiscoveryScans.AsNoTracking().OrderByDescending(s => s.CreatedAt).Take(20)
                .Select(s => new { Scan = s, Alive = s.Hosts.Count }).ToListAsync(ct))
            .Select(x => ToDto(x.Scan, x.Alive)).ToList());

    /// <summary>Nuova scansione: validata la rete (IPv4, /22–/32), gli agenti vengono avvisati e il primo la esegue.</summary>
    private static async Task<Results<Created<DiscoveryScanDto>, ValidationProblem>> CreateScanAsync(
        DiscoveryScanCreateDto dto, ClaimsPrincipal user, VedettaVipDbContext db, AgentNotifier agents, TimeProvider time, CancellationToken ct)
    {
        var cidr = dto.Cidr.Trim();
        if (!cidr.Contains('/'))
            cidr += "/24"; // "192.0.2.0" → la /24
        if (ParseCidr(cidr) is not { } parsed)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                { [nameof(dto.Cidr)] = ["Rete non valida: IPv4 con prefisso da /22 a /32, es. 192.0.2.0/24."] });

        var scan = new DiscoveryScan
        {
            Id = Guid.CreateVersion7(), Cidr = parsed, CustomerId = dto.CustomerId, MapId = dto.MapId,
            Status = DiscoveryScanStatus.Pending, CreatedAt = time.GetUtcNow(), CreatedBy = user.Identity?.Name
        };
        db.DiscoveryScans.Add(scan);
        await db.SaveChangesAsync(ct);
        await agents.ScanRequestedAsync(scan.Id);
        return TypedResults.Created($"/api/discovery/scans/{scan.Id}", ToDto(scan, 0));
    }

    /// <summary>"a.b.c.d/n" normalizzata all'indirizzo di rete; null se non valida o più ampia di /22.</summary>
    public static string? ParseCidr(string text)
    {
        var parts = text.Split('/');
        if (parts.Length != 2 || !System.Net.IPAddress.TryParse(parts[0], out var ip)
            || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || !int.TryParse(parts[1], out var prefix) || prefix is < 22 or > 32)
            return null;
        var b = ip.GetAddressBytes();
        var value = (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]) & (prefix == 0 ? 0 : uint.MaxValue << (32 - prefix));
        return $"{value >> 24}.{(value >> 16) & 255}.{(value >> 8) & 255}.{value & 255}/{prefix}";
    }

    private static async Task<Results<NoContent, NotFound>> DeleteScanAsync(Guid id, VedettaVipDbContext db, CancellationToken ct) =>
        await db.DiscoveryScans.Where(s => s.Id == id).ExecuteDeleteAsync(ct) == 0 ? TypedResults.NotFound() : TypedResults.NoContent();

    /// <summary>
    /// Un agente prende la scansione: solo se è ancora in attesa (o già sua). Riceve le community dei profili SNMP da
    /// provare (la propria la aggiunge lui).
    /// </summary>
    private static async Task<Results<Ok<AgentScanRequestDto>, NotFound, Conflict>> ClaimScanAsync(
        Guid id, HttpContext http, VedettaVipDbContext db, SecretProtector secrets, CancellationToken ct)
    {
        var agentId = http.Request.Headers["X-Agent-Id"].ToString();
        var scan = await db.DiscoveryScans.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (scan is null)
            return TypedResults.NotFound();
        if (scan.Status != DiscoveryScanStatus.Pending && scan.AgentId != agentId)
            return TypedResults.Conflict();

        scan.Status = DiscoveryScanStatus.Running;
        scan.AgentId = agentId;
        await db.SaveChangesAsync(ct);

        var communities = (await db.SnmpCredentials.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct))
            .Select(c => secrets.Unprotect(c.CommunityProtected) is { } community ? new ScanCommunityDto(c.Id, community) : null)
            .OfType<ScanCommunityDto>().ToList();
        return TypedResults.Ok(new AgentScanRequestDto(scan.Id, scan.Cidr, communities));
    }

    /// <summary>Avanzamento o esito; all'esito gli host trovati sostituiscono quelli precedenti della scansione.</summary>
    private static async Task<Results<NoContent, NotFound>> PostScanResultAsync(
        Guid id, AgentScanResultDto result, VedettaVipDbContext db, TimeProvider time, CancellationToken ct)
    {
        var scan = await db.DiscoveryScans.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (scan is null)
            return TypedResults.NotFound();

        (scan.Status, scan.Scanned, scan.Total, scan.Error) = (result.Status, result.Scanned, result.Total, result.Error);
        if (result.Status is DiscoveryScanStatus.Done or DiscoveryScanStatus.Failed)
            scan.CompletedAt = time.GetUtcNow();
        if (result.Status == DiscoveryScanStatus.Done)
        {
            await db.Set<DiscoveryScanHost>().Where(h => h.ScanId == id).ExecuteDeleteAsync(ct);
            db.Set<DiscoveryScanHost>().AddRange(result.Hosts.Select(h => new DiscoveryScanHost
            {
                Id = Guid.CreateVersion7(), ScanId = id, Address = h.Address, RttMs = h.RttMs, DnsName = h.DnsName, SysName = h.SysName,
                SysDescr = h.SysDescr, SnmpProfileId = h.SnmpProfileId, SnmpFallback = h.SnmpFallback,
                OpenPorts = h.OpenPorts.Count > 0 ? string.Join(',', h.OpenPorts) : null
            }));
        }
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    /// <summary>Sostituisce i vicini del device con quelli dell'ultima lettura (delete + insert in transazione).</summary>
    internal static async Task<Results<NoContent, NotFound>> PostNeighborsAsync(
        AgentNeighborsReportDto report, VedettaVipDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (!await db.Devices.AnyAsync(d => d.Id == report.DeviceId, ct))
            return TypedResults.NotFound();

        var now = time.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.DeviceNeighbors.Where(n => n.DeviceId == report.DeviceId).ExecuteDeleteAsync(ct);
        db.DeviceNeighbors.AddRange(report.Neighbors.Select(n => new DeviceNeighbor
        {
            Id = Guid.CreateVersion7(), DeviceId = report.DeviceId, Protocol = n.Protocol, LocalInterface = n.LocalInterface,
            LocalIfIndex = n.LocalIfIndex, Identity = n.Identity, Address = n.Address, MacAddress = n.MacAddress,
            RemoteInterface = n.RemoteInterface, Platform = n.Platform, Version = n.Version, Board = n.Board,
            Capabilities = n.Capabilities, SeenAt = now
        }));
        if (report.Arp is { } arp)
        {
            await db.DeviceArpEntries.Where(a => a.DeviceId == report.DeviceId).ExecuteDeleteAsync(ct);
            db.DeviceArpEntries.AddRange(arp.Select(a => new DeviceArpEntry
            {
                Id = Guid.CreateVersion7(), DeviceId = report.DeviceId, Address = a.Address, MacAddress = a.MacAddress,
                Interface = a.Interface, IfIndex = a.IfIndex, HostName = a.HostName, Comment = a.Comment, SeenAt = now
            }));
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<DiscoveryResultDto>> GetAsync(VedettaVipDbContext db, CancellationToken ct)
    {
        var (plan, lastSeen, scanned) = await PlanAsync(db, ct);
        return TypedResults.Ok(new DiscoveryResultDto(lastSeen, scanned, plan.Devices, plan.Links, plan.Ignored));
    }

    private static async Task<((List<DiscoveryDeviceProposalDto> Devices, List<DiscoveryLinkProposalDto> Links, int Ignored) Plan,
        DateTimeOffset? LastSeen, int Scanned)> PlanAsync(VedettaVipDbContext db, CancellationToken ct)
    {
        var interfaced = await db.DeviceInterfaces.AsNoTracking().Select(i => i.DeviceId).Distinct().ToListAsync(ct);
        var devices = (await db.Devices.AsNoTracking().Select(d => new { d.Id, d.Name, d.Address, d.CustomerId }).ToListAsync(ct))
            .Select(d => new DiscoveryPlanner.DeviceInfo(d.Id, d.Name, d.Address, d.CustomerId, interfaced.Contains(d.Id))).ToList();
        var nodes = await db.MapNodes.AsNoTracking().Where(n => n.DeviceId != null)
            .Select(n => new DiscoveryPlanner.NodeInfo(n.DeviceId!.Value, n.MapId, n.Map!.Name)).ToListAsync(ct);
        var links = await db.MapLinks.AsNoTracking()
            .Where(l => l.FromNode!.DeviceId != null && l.ToNode!.DeviceId != null)
            .Select(l => new DiscoveryPlanner.LinkInfo(l.FromNode!.DeviceId!.Value, l.ToNode!.DeviceId!.Value)).ToListAsync(ct);
        var interfaces = await db.DeviceInterfaces.AsNoTracking()
            .Select(i => new DiscoveryPlanner.InterfaceInfo(i.DeviceId, i.IfIndex, i.Name, i.SpeedBps)).ToListAsync(ct);
        var rows = await db.DeviceNeighbors.AsNoTracking().ToListAsync(ct);
        var neighbors = rows.Select(n => new DiscoveryPlanner.NeighborInfo(n.DeviceId, new NeighborDto(n.Protocol, n.LocalInterface,
            n.LocalIfIndex, n.Identity, n.Address, n.MacAddress, n.RemoteInterface, n.Platform, n.Version, n.Board, n.Capabilities))).ToList();
        var ignored = (await db.DiscoveryIgnores.AsNoTracking().Select(i => i.Key).ToListAsync(ct)).ToHashSet();
        var arp = (await db.DeviceArpEntries.AsNoTracking().ToListAsync(ct))
            .Select(a => new DiscoveryPlanner.ArpInfo(a.DeviceId, new ArpEntryDto(a.Address, a.MacAddress, a.Interface, a.IfIndex, a.HostName, a.Comment)))
            .ToList();
        // Per ogni rete conta l'ultima scansione completata
        var scans = await db.DiscoveryScans.AsNoTracking().Include(s => s.Hosts)
            .Where(s => s.Status == DiscoveryScanStatus.Done).OrderByDescending(s => s.CompletedAt).ToListAsync(ct);
        var scanHosts = scans.DistinctBy(s => s.Cidr)
            .SelectMany(s => s.Hosts.Select(h => new DiscoveryPlanner.ScanHostInfo(s.Id, s.CustomerId, s.MapId, new ScanHostDto(h.Address, h.RttMs,
                h.DnsName, h.SysName, h.SysDescr, h.SnmpProfileId, h.SnmpFallback,
                (h.OpenPorts ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList()))))
            .ToList();

        var plan = DiscoveryPlanner.Plan(devices, nodes, links, interfaces, neighbors, ignored, arp, scanHosts);
        return (plan, rows.Count > 0 ? rows.Max(r => r.SeenAt) : null, rows.Select(r => r.DeviceId).Distinct().Count());
    }

    /// <summary>
    /// Crea i dispositivi scelti (con nodo sulla mappa, sotto quelli esistenti, e link verso chi li ha visti se è sulla
    /// stessa mappa) e i link scelti, in un unico SaveChanges. Le proposte sono ricalcolate qui: si accetta solo ciò che
    /// è ancora valido (es. un link già disegnato nel frattempo viene saltato con un avviso).
    /// </summary>
    private static async Task<Results<Ok<DiscoveryAcceptResultDto>, ValidationProblem, ProblemHttpResult>> AcceptAsync(
        DiscoveryAcceptDto dto, VedettaVipDbContext db, AgentNotifier agents, CancellationToken ct)
    {
        var (plan, _, _) = await PlanAsync(db, ct);
        var proposals = plan.Devices.ToDictionary(d => d.Key);
        var linkProposals = plan.Links.ToDictionary(l => l.Key);
        var warnings = new List<string>();
        var existingAddresses = (await db.Devices.AsNoTracking().Select(d => d.Address).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var maps = await db.Maps.AsNoTracking().ToDictionaryAsync(m => m.Id, ct);
        var nodes = await db.MapNodes.Where(n => n.DeviceId != null).ToListAsync(ct); // tracciati: i nuovi si aggiungono qui
        var interfaces = (await db.DeviceInterfaces.AsNoTracking().ToListAsync(ct)).ToLookup(i => i.DeviceId);
        var errors = new Dictionary<string, string[]>();
        int devicesCreated = 0, linksCreated = 0;

        Data.Entities.MapNode? NodeOf(Guid deviceId, Guid mapId) => nodes.FirstOrDefault(n => n.DeviceId == deviceId && n.MapId == mapId);

        void AddLink(Guid mapId, Guid fromDevice, Guid toDevice, Guid? sourceDevice, string? sourceInterface, int? sourceIfIndex, long? speed)
        {
            if (NodeOf(fromDevice, mapId) is not { } from || NodeOf(toDevice, mapId) is not { } to)
                return;
            var iface = sourceDevice is { } sd
                ? sourceIfIndex is { } x
                    ? interfaces[sd].FirstOrDefault(i => i.IfIndex == x)
                    : DiscoveryPlanner.FindInterface(interfaces[sd].ToList(), sourceInterface, i => i.Name)
                : null;
            db.MapLinks.Add(new MapLink
            {
                Id = Guid.CreateVersion7(), MapId = mapId, FromNodeId = from.Id, ToNodeId = to.Id,
                DeviceId = iface is null ? null : sourceDevice, IfIndex = iface?.IfIndex,
                SpeedBps = speed ?? iface?.SpeedBps ?? DiscoveryPlanner.DefaultSpeedBps
            });
            linksCreated++;
        }

        foreach (var d in dto.Devices)
        {
            if (!proposals.TryGetValue(d.Key, out var proposal))
            {
                warnings.Add($"\"{d.Name}\": proposta non più valida (già aggiunto o non più visto)");
                continue;
            }
            var address = d.Address.Trim();
            if (!DeviceEndpoints.IsValidAddress(address))
            {
                errors[d.Key] = [$"\"{d.Name}\": indirizzo non valido \"{address}\""];
                continue;
            }
            if (!existingAddresses.Add(address))
            {
                warnings.Add($"\"{d.Name}\": esiste già un dispositivo con l'indirizzo {address}");
                continue;
            }

            var device = new Device
            {
                Id = Guid.CreateVersion7(), Name = d.Name.Trim(), Address = address, Type = d.Type, Enabled = true,
                SnmpVersion = d.Snmp ? SnmpVersion.V2c : SnmpVersion.None, SnmpCredentialId = d.Snmp ? d.SnmpCredentialId : null,
                RouterOsApiEnabled = d.RouterOs, CustomerId = d.CustomerId, ParentDeviceId = d.ParentId
            };
            db.Devices.Add(device);
            devicesCreated++;

            if (d.MapId is { } mapId && maps.TryGetValue(mapId, out var map))
            {
                var occupied = nodes.Where(n => n.MapId == mapId).Select(n => (n.X, n.Y))
                    .Concat(db.ChangeTracker.Entries<Data.Entities.MapNode>().Where(e => e.State == EntityState.Added && e.Entity.MapId == mapId)
                        .Select(e => (e.Entity.X, e.Entity.Y)))
                    .Distinct().ToList();
                var (x, y) = MapPlacement.NextBelow(occupied, map.GridSize);
                var node = new Data.Entities.MapNode
                {
                    Id = Guid.CreateVersion7(), MapId = mapId, Kind = MapNodeKind.Device, DeviceId = device.Id,
                    X = x, Y = y, LabelTemplate = Data.Entities.MapNode.DefaultLabelTemplate
                };
                db.MapNodes.Add(node);
                nodes.Add(node);

                // Link verso chi l'ha visto come vicino (non via ARP: non è detto che ci sia un cavo diretto),
                // se è sulla stessa mappa (sorgente di traffico: la sua interfaccia)
                foreach (var s in proposal.SeenBy.Where(s => s.Protocol != "arp").DistinctBy(s => s.DeviceId))
                    AddLink(mapId, s.DeviceId, device.Id, s.DeviceId, s.LocalInterface, null, null);
            }
        }

        foreach (var key in dto.LinkKeys.Distinct())
        {
            if (!linkProposals.TryGetValue(key, out var l))
            {
                warnings.Add("un link proposto non è più valido (già disegnato o non più visto)");
                continue;
            }
            if (l.MapId is not { } mapId)
            {
                warnings.Add($"{l.FromName} – {l.ToName}: nessuna mappa in comune");
                continue;
            }
            AddLink(mapId, l.FromDeviceId, l.ToDeviceId, l.SourceDeviceId, null, l.SourceIfIndex, l.SpeedBps);
        }

        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);
        if (await db.TrySaveChangesAsync(ct) is { } problem)
            return problem;
        if (devicesCreated > 0 || linksCreated > 0)
            await agents.TargetsChangedAsync();
        return TypedResults.Ok(new DiscoveryAcceptResultDto(devicesCreated, linksCreated, warnings));
    }

    /// <summary>
    /// Elimina le proposte di dispositivi: cancella le letture da cui nascono (righe ARP e vicini dei router che le hanno
    /// viste, host della scansione). A differenza di "Ignora" non resta memoria: se l'host c'è ancora torna alla prossima
    /// lettura dei router (30 minuti, o "Scopri ora") o alla prossima scansione.
    /// </summary>
    private static async Task<Ok<DiscoveryForgetResultDto>> ForgetAsync(DiscoveryForgetDto dto, VedettaVipDbContext db, CancellationToken ct)
    {
        var (plan, _, _) = await PlanAsync(db, ct);
        var keys = dto.Keys.ToHashSet();
        int arp = 0, neighbors = 0, hosts = 0;
        foreach (var p in plan.Devices.Where(p => keys.Contains(p.Key)))
        {
            var (address, mac) = (p.Address, p.MacAddress);
            var arpSeers = p.SeenBy.Where(s => s.Protocol == "arp").Select(s => s.DeviceId).Distinct().ToList();
            var neighborSeers = p.SeenBy.Where(s => s.Protocol != "arp").Select(s => s.DeviceId).Distinct().ToList();
            if (address is not null && arpSeers.Count > 0)
                arp += await db.DeviceArpEntries
                    .Where(a => arpSeers.Contains(a.DeviceId) && a.Address == address && (mac == null || a.MacAddress == mac))
                    .ExecuteDeleteAsync(ct);
            if (neighborSeers.Count > 0)
                neighbors += await db.DeviceNeighbors
                    .Where(n => neighborSeers.Contains(n.DeviceId) && ((address != null && n.Address == address) || (mac != null && n.MacAddress == mac)))
                    .ExecuteDeleteAsync(ct);
            if (p.ScanId is { } scanId && address is not null)
                hosts += await db.Set<DiscoveryScanHost>().Where(h => h.ScanId == scanId && h.Address == address).ExecuteDeleteAsync(ct);
        }
        return TypedResults.Ok(new DiscoveryForgetResultDto(arp, neighbors, hosts));
    }

    /// <summary>Ignora (o ripristina, con Ignore = false) proposte: le ignorate non vengono più mostrate.</summary>
    private static async Task<NoContent> IgnoreAsync(DiscoveryIgnoreDto dto, VedettaVipDbContext db, TimeProvider time, CancellationToken ct)
    {
        var keys = dto.Keys.Where(k => k.Length is > 0 and <= 512).Distinct().ToList();
        if (dto.Ignore)
        {
            var existing = await db.DiscoveryIgnores.Where(i => keys.Contains(i.Key)).Select(i => i.Key).ToListAsync(ct);
            db.DiscoveryIgnores.AddRange(keys.Except(existing).Select(k => new DiscoveryIgnore { Key = k, CreatedAt = time.GetUtcNow() }));
            await db.SaveChangesAsync(ct);
        }
        else
        {
            await db.DiscoveryIgnores.Where(i => dto.Keys.Count == 0 || keys.Contains(i.Key)).ExecuteDeleteAsync(ct);
        }
        return TypedResults.NoContent();
    }
}
