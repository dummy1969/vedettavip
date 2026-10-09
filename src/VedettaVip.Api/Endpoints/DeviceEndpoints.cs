// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Net;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VedettaVip.Api.Data;
using VedettaVip.Api.Security;
using VedettaVip.Api.Data.Entities;
using VedettaVip.Api.Options;
using VedettaVip.Api.Services;
using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;

namespace VedettaVip.Api.Endpoints;

public static class DeviceEndpoints
{
    private const int MaxParentDepth = 64;

    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var devices = app.MapGroup("/api/devices").WithTags("Devices");

        devices.MapGet("/", GetDevicesAsync);
        devices.MapGet("/overview", GetOverviewAsync);
        devices.MapGet("/{id:guid}", GetDeviceAsync);
        devices.MapPost("/", CreateDeviceAsync).RequireOperator();
        devices.MapPut("/{id:guid}", UpdateDeviceAsync).RequireOperator();
        devices.MapDelete("/{id:guid}", DeleteDeviceAsync).RequireOperator();
        devices.MapGet("/{id:guid}/interfaces", GetInterfacesAsync);
        devices.MapPost("/{id:guid}/interfaces/refresh", RefreshInterfacesAsync).RequireOperator();

        return app;
    }

    private static async Task<Ok<List<DeviceDto>>> GetDevicesAsync(VedettaVipDbContext db, CancellationToken ct)
    {
        var devices = await db.Devices
            .AsNoTracking()
            .OrderBy(d => d.Name)
            .Select(d => ToDto(d))
            .ToListAsync(ct);

        return TypedResults.Ok(devices);
    }

    /// <summary>Dispositivi con stato corrente e mappe di appartenenza, per la pagina di gestione.</summary>
    private static async Task<Ok<List<DeviceOverviewDto>>> GetOverviewAsync(
        VedettaVipDbContext db, TimeProvider time, IOptions<AgentOptions> agentOptions, ActiveMaintenance maintenance, CancellationToken ct)
    {
        var staleBefore = MapEndpoints.StaleBefore(time, agentOptions.Value);

        var devices = await db.Devices
            .AsNoTracking()
            .Include(d => d.Status)
            .OrderBy(d => d.Name)
            .ToListAsync(ct);

        var mapsByDevice = (await db.MapNodes
                .AsNoTracking()
                .Where(n => n.DeviceId != null)
                .OrderBy(n => n.Map!.Name)
                .Select(n => new { DeviceId = n.DeviceId!.Value, n.MapId, MapName = n.Map!.Name })
                .ToListAsync(ct))
            .ToLookup(x => x.DeviceId, x => new MapRefDto(x.MapId, x.MapName));

        var inMaintenance = await maintenance.LoadAsync(db, ct);
        var overview = devices.Select(d =>
        {
            var s = d.Status;
            var state = d.Enabled && s is not null && s.LastReportAt >= staleBefore ? s.State : NodeState.Unknown;
            return new DeviceOverviewDto(ToDto(d), state, s?.Since, s?.LastReportAt, s?.LastRttMs, s?.SnmpOk, s?.AgentId,
                mapsByDevice[d.Id].ToList(), inMaintenance(d.Id));
        }).ToList();

        return TypedResults.Ok(overview);
    }

    private static async Task<Results<Ok<DeviceDto>, NotFound>> GetDeviceAsync(Guid id, VedettaVipDbContext db, CancellationToken ct)
    {
        var device = await db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);
        return device is null ? TypedResults.NotFound() : TypedResults.Ok(ToDto(device));
    }

    /// <summary>
    /// Crea il device; con <paramref name="mapId"/> lo aggiunge anche a quella mappa (sotto i nodi esistenti),
    /// nello stesso SaveChanges: o nascono entrambi o nessuno dei due.
    /// </summary>
    private static async Task<Results<Created<DeviceDto>, ValidationProblem, ProblemHttpResult>> CreateDeviceAsync(
        DeviceUpsertDto dto, VedettaVipDbContext db, AgentNotifier agents, MapNotifier notifier, CancellationToken ct, Guid? mapId = null)
    {
        var id = Guid.CreateVersion7();
        var errors = await ValidateAsync(db, dto, id, ct) ?? [];
        var map = mapId is { } m ? await db.Maps.AsNoTracking().FirstOrDefaultAsync(x => x.Id == m, ct) : null;
        if (mapId is not null && map is null)
            errors["mapId"] = ["La mappa non esiste."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var device = new Device { Id = id, Name = "", Address = "" };
        Apply(device, dto);
        db.Devices.Add(device);

        if (map is not null)
        {
            var occupied = (await db.MapNodes.AsNoTracking().Where(n => n.MapId == map.Id).Select(n => new { n.X, n.Y }).ToListAsync(ct))
                .Select(n => (n.X, n.Y)).ToList();
            var (x, y) = MapPlacement.NextBelow(occupied, map.GridSize);
            db.MapNodes.Add(new Data.Entities.MapNode
            {
                Id = Guid.CreateVersion7(), MapId = map.Id, Kind = MapNodeKind.Device, DeviceId = id,
                X = x, Y = y, LabelTemplate = Data.Entities.MapNode.DefaultLabelTemplate
            });
        }

        if (await db.TrySaveChangesAsync(ct) is { } saveProblem)
            return saveProblem;

        await agents.TargetsChangedAsync();
        if (map is not null)
            await notifier.MapsChangedAsync(db, [map.Id]);
        return TypedResults.Created($"/api/devices/{device.Id}", ToDto(device));
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem, ProblemHttpResult>> UpdateDeviceAsync(
        Guid id, DeviceUpsertDto dto, VedettaVipDbContext db, AgentNotifier agents, MapNotifier notifier, CancellationToken ct)
    {
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (device is null)
            return TypedResults.NotFound();

        if (await ValidateAsync(db, dto, id, ct) is { } errors)
            return TypedResults.ValidationProblem(errors);

        Apply(device, dto);

        if (await db.TrySaveChangesAsync(ct) is { } saveProblem)
            return saveProblem;

        await agents.TargetsChangedAsync();
        await notifier.DevicesChangedAsync(db, [id]); // nome, indirizzo, icona e abilitazione si vedono sulle mappe
        return TypedResults.NoContent();
    }

    /// <summary>
    /// Le FK verso Device sono RESTRICT: un dispositivo presente su mappe (nodi o link di traffico) non si elimina,
    /// si risponde 409 con l'elenco delle mappe. Anche gli eventi (storico dei cambi di stato) bloccano l'eliminazione,
    /// salvo <paramref name="purgeEvents"/> = true: eventi e dispositivo vengono cancellati nella stessa transazione.
    /// Stato, inventario interfacce (CASCADE) e metriche grezze (senza FK, scadono con la retention) non bloccano.
    /// </summary>
    private static async Task<Results<NoContent, NotFound, Conflict<DeviceInUseDto>, ProblemHttpResult>> DeleteDeviceAsync(
        Guid id, VedettaVipDbContext db, AgentNotifier agents, CancellationToken ct, bool purgeEvents = false)
    {
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (device is null)
            return TypedResults.NotFound();

        var maps = await db.Maps
            .AsNoTracking()
            .Where(m => m.Nodes.Any(n => n.DeviceId == id) || m.Links.Any(l => l.DeviceId == id))
            .OrderBy(m => m.Name)
            .Select(m => new MapRefDto(m.Id, m.Name))
            .ToListAsync(ct);

        var eventCount = await db.Events.CountAsync(e => e.DeviceId == id, ct);

        if (maps.Count > 0)
            return TypedResults.Conflict(new DeviceInUseDto(id,
                $"Il dispositivo {device.Name} compare su {Plural(maps.Count, "mappa", "mappe")}: rimuoverlo prima dalle mappe.",
                maps, eventCount));

        if (eventCount > 0 && !purgeEvents)
            return TypedResults.Conflict(new DeviceInUseDto(id,
                $"Il dispositivo {device.Name} ha {Plural(eventCount, "evento registrato", "eventi registrati")} (storico dei cambi di stato).",
                maps, eventCount));

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (eventCount > 0)
            await db.Events.Where(e => e.DeviceId == id).ExecuteDeleteAsync(ct);

        // I dispositivi figli restano: ParentDeviceId diventa NULL (ON DELETE SET NULL)
        db.Devices.Remove(device);

        if (await db.TrySaveChangesAsync(ct) is { } saveProblem)
            return saveProblem; // la transazione non confermata annulla anche la cancellazione degli eventi

        await tx.CommitAsync(ct);
        await agents.TargetsChangedAsync();
        return TypedResults.NoContent();
    }

    private static string Plural(int count, string singular, string plural) => $"{count} {(count == 1 ? singular : plural)}";

    /// <summary>Interfacce dall'ultimo inventario SNMP (vuoto se mai letto: device senza SNMP v2c o non ancora interrogato).</summary>
    private static async Task<Results<Ok<DeviceInterfacesDto>, NotFound>> GetInterfacesAsync(Guid id, VedettaVipDbContext db, CancellationToken ct)
    {
        if (!await db.Devices.AnyAsync(d => d.Id == id, ct))
            return TypedResults.NotFound();

        var rows = await db.DeviceInterfaces
            .AsNoTracking()
            .Where(i => i.DeviceId == id)
            .OrderBy(i => i.IfIndex)
            .ToListAsync(ct);

        return TypedResults.Ok(new DeviceInterfacesDto(
            id,
            rows.Count > 0 ? rows.Max(r => r.UpdatedAt) : null,
            rows.Select(r => new DeviceInterfaceDto(r.IfIndex, r.Name, r.Alias, r.SpeedBps, r.OperStatus)).ToList()));
    }

    /// <summary>Chiede all'agente un inventario immediato; il risultato arriva in pochi secondi su GET .../interfaces.</summary>
    private static async Task<Results<Accepted, NotFound>> RefreshInterfacesAsync(
        Guid id, VedettaVipDbContext db, AgentNotifier agents, CancellationToken ct)
    {
        if (!await db.Devices.AnyAsync(d => d.Id == id, ct))
            return TypedResults.NotFound();

        await agents.InterfacesRequestedAsync(id);
        return TypedResults.Accepted($"/api/devices/{id}/interfaces");
    }

    private static DeviceDto ToDto(Device d) => new(
        d.Id, d.Name, d.Address, d.Type, d.Icon, d.SnmpVersion, d.SnmpCredentialId,
        d.RouterOsApiEnabled, d.ParentDeviceId, d.Enabled,
        d.DownAfterFailures, d.UpAfterSuccesses, d.SnmpDegradedAfterFailures, d.CustomerId, d.RttThresholdMs, d.LossThresholdPct,
        d.RouterOsCredentialId, d.CpuThresholdPct, d.TemperatureThresholdC, d.Vendor);

    private static void Apply(Device d, DeviceUpsertDto dto)
    {
        d.Name = dto.Name.Trim();
        d.Address = dto.Address.Trim();
        d.Type = dto.Type;
        d.Icon = string.IsNullOrWhiteSpace(dto.Icon) ? null : dto.Icon.Trim();
        d.SnmpVersion = dto.SnmpVersion;
        d.SnmpCredentialId = dto.SnmpCredentialId;
        d.RouterOsApiEnabled = dto.RouterOsApiEnabled;
        d.ParentDeviceId = dto.ParentDeviceId;
        d.Enabled = dto.Enabled;
        d.DownAfterFailures = dto.DownAfterFailures;
        d.UpAfterSuccesses = dto.UpAfterSuccesses;
        d.SnmpDegradedAfterFailures = dto.SnmpDegradedAfterFailures;
        d.CustomerId = dto.CustomerId;
        d.RttThresholdMs = dto.RttThresholdMs;
        d.LossThresholdPct = dto.LossThresholdPct;
        d.RouterOsCredentialId = dto.RouterOsCredentialId;
        d.CpuThresholdPct = dto.CpuThresholdPct;
        d.TemperatureThresholdC = dto.TemperatureThresholdC;
        d.Vendor = VendorOf(dto.Vendor, dto.RouterOsApiEnabled);
    }

    /// <summary>L'API RouterOS esiste solo sui MikroTik: abilitarla fissa il produttore.</summary>
    public static DeviceVendor VendorOf(DeviceVendor vendor, bool routerOsApiEnabled) =>
        routerOsApiEnabled ? DeviceVendor.MikroTik : vendor;

    /// <summary>IPv4/IPv6 o hostname DNS valido.</summary>
    internal static bool IsValidAddress(string address) =>
        IPAddress.TryParse(address, out _) || Uri.CheckHostName(address) == UriHostNameType.Dns;

    private static async Task<Dictionary<string, string[]>?> ValidateAsync(
        VedettaVipDbContext db, DeviceUpsertDto dto, Guid selfId, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(dto.Name))
            errors[nameof(dto.Name)] = ["Il nome è obbligatorio."];

        if (!IsValidAddress(dto.Address?.Trim() ?? ""))
            errors[nameof(dto.Address)] = ["Indirizzo non valido: atteso un IPv4/IPv6 o un hostname."];

        if (dto.CustomerId is { } customerId && !await db.Customers.AnyAsync(c => c.Id == customerId, ct))
            errors[nameof(dto.CustomerId)] = ["Il cliente non esiste."];

        if (dto.SnmpCredentialId is { } credentialId && !await db.SnmpCredentials.AnyAsync(c => c.Id == credentialId, ct))
            errors[nameof(dto.SnmpCredentialId)] = ["Il profilo SNMP non esiste."];

        if (dto.RouterOsCredentialId is { } rosId && !await db.RouterOsCredentials.AnyAsync(c => c.Id == rosId, ct))
            errors[nameof(dto.RouterOsCredentialId)] = ["Il profilo RouterOS non esiste."];

        if (dto.ParentDeviceId is { } parentId)
        {
            if (parentId == selfId)
                errors[nameof(dto.ParentDeviceId)] = ["Un dispositivo non può dipendere da se stesso."];
            else if (!await db.Devices.AnyAsync(d => d.Id == parentId, ct))
                errors[nameof(dto.ParentDeviceId)] = ["Il dispositivo padre non esiste."];
            else if (await CreatesCycleAsync(db, selfId, parentId, ct))
                errors[nameof(dto.ParentDeviceId)] = ["La dipendenza creerebbe un ciclo."];
        }

        return errors.Count > 0 ? errors : null;
    }

    /// <summary>Risale la catena dei padri a partire da <paramref name="parentId"/>: se incontra <paramref name="selfId"/> c'è un ciclo.</summary>
    private static async Task<bool> CreatesCycleAsync(VedettaVipDbContext db, Guid selfId, Guid parentId, CancellationToken ct)
    {
        Guid? current = parentId;
        for (var depth = 0; current is { } id; depth++)
        {
            if (id == selfId || depth >= MaxParentDepth)
                return true;
            current = await db.Devices.Where(d => d.Id == id).Select(d => d.ParentDeviceId).FirstOrDefaultAsync(ct);
        }
        return false;
    }
}
