// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.EntityFrameworkCore;
using VedettaVip.Api.Data;
using VedettaVip.Api.Data.Entities;
using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;

namespace VedettaVip.Api.Services;

/// <summary>
/// Interruzioni aperte di un gruppo di device e servizi, caricate tracciate: aperture, chiusure e riallineamenti
/// lavorano in memoria e finiscono nel SaveChanges del chiamante (stessa transazione degli eventi). Aprire ciò che è già
/// aperto o chiudere ciò che non lo è non fa nulla: i chiamanti possono sia seguire le transizioni sia riallineare lo
/// stato a ogni report (device riabilitato ancora giù, storico ricostruito a metà).
/// </summary>
public sealed class OutageBook
{
    public const string PingKey = "ping";
    public const string SnmpKey = "snmp";
    public const string DisabledNote = "monitoraggio disattivato";

    /// <summary>Servizi che seguono lo stato del device (Down = ping, Partial = SNMP).</summary>
    public static readonly OutageService[] StateServices = [OutageService.Ping, OutageService.Snmp];
    public static readonly OutageService[] WatchServices = [OutageService.Interface, OutageService.WireGuard];
    public static readonly OutageService[] ThresholdServices =
        [OutageService.Latency, OutageService.Loss, OutageService.Cpu, OutageService.Temperature, OutageService.Link];

    private readonly VedettaVipDbContext db;
    private readonly Dictionary<(Guid DeviceId, string Key), Outage> open;
    private readonly List<Outage> touched = [];

    private OutageBook(VedettaVipDbContext db, IEnumerable<Outage> open)
    {
        this.db = db;
        this.open = open.ToDictionary(o => (o.DeviceId, o.Key));
    }

    /// <summary>Carica le interruzioni aperte dei servizi indicati; <paramref name="deviceIds"/> null = tutti i device.</summary>
    public static async Task<OutageBook> LoadAsync(
        VedettaVipDbContext db, IReadOnlyCollection<Guid>? deviceIds, IReadOnlyCollection<OutageService> services, CancellationToken ct)
    {
        var query = db.Outages.Where(o => o.EndedAt == null && services.Contains(o.Service));
        if (deviceIds is not null)
            query = query.Where(o => deviceIds.Contains(o.DeviceId));
        return new OutageBook(db, await query.ToListAsync(ct));
    }

    /// <summary>Interruzioni ancora aperte dopo le modifiche fatte finora.</summary>
    public IEnumerable<Outage> OpenOutages => open.Values;

    public bool IsOpen(Guid deviceId, string key) => open.ContainsKey((deviceId, key));

    /// <summary>Apre un'interruzione, se non è già aperta.</summary>
    public void Open(Guid deviceId, OutageService service, string key, string? detail, DateTimeOffset at)
    {
        if (open.ContainsKey((deviceId, key)))
            return;
        var outage = new Outage
        {
            Id = Guid.CreateVersion7(), DeviceId = deviceId, Service = service, Key = key,
            Detail = Truncate(detail, 256), StartedAt = at.ToUniversalTime()
        };
        db.Outages.Add(outage);
        open[(deviceId, key)] = outage;
        touched.Add(outage);
    }

    /// <summary>Chiude l'interruzione aperta, se c'è. <paramref name="detailSuffix"/> si aggiunge al dettaglio (es. il picco).</summary>
    public bool Close(Guid deviceId, string key, DateTimeOffset at, string? note = null, string? detailSuffix = null)
    {
        if (!open.Remove((deviceId, key), out var outage))
            return false;
        var end = at.ToUniversalTime();
        outage.EndedAt = end < outage.StartedAt ? outage.StartedAt : end;
        outage.EndNote = Truncate(note, 128);
        if (detailSuffix is not null)
            outage.Detail = Truncate(outage.Detail is null ? detailSuffix : $"{outage.Detail}; {detailSuffix}", 256);
        touched.Add(outage);
        return true;
    }

    /// <summary>
    /// Riallinea ping e SNMP allo stato del device: Down = ping giù, Partial = SNMP giù (il ping risponde), Up = nessuna
    /// delle due. Unknown non decide nulla (agente fermo ≠ device ripristinato).
    /// </summary>
    public void SyncState(Guid deviceId, NodeState state, DateTimeOffset at)
    {
        if (state == NodeState.Unknown)
            return;
        // Prima le chiusure: Partial → Down chiude SNMP e apre il ping allo stesso istante
        if (state != NodeState.Down) Close(deviceId, PingKey, at);
        if (state != NodeState.Partial) Close(deviceId, SnmpKey, at);
        if (state == NodeState.Down) Open(deviceId, OutageService.Ping, PingKey, "non risponde al ping", at);
        if (state == NodeState.Partial) Open(deviceId, OutageService.Snmp, SnmpKey, "risponde al ping ma non a SNMP", at);
    }

    /// <summary>
    /// Segna la manutenzione sulle interruzioni aperte o chiuse in questo giro (finestra attiva adesso sul device), così
    /// un'interruzione è "in manutenzione" se la finestra copriva l'inizio o la fine. Carica le finestre solo se serve.
    /// </summary>
    public async Task TagMaintenanceAsync(Func<CancellationToken, Task<Func<Guid, string?>>> load, CancellationToken ct)
    {
        var pending = touched.Where(o => o.Maintenance is null).ToList();
        if (pending.Count == 0)
            return;
        var maintenanceOf = await load(ct);
        foreach (var o in pending)
            o.Maintenance = Truncate(maintenanceOf(o.DeviceId), 128);
    }

    /// <summary>
    /// Device appena disabilitati (Enabled da true a false nel ChangeTracker): le loro interruzioni aperte si chiudono
    /// adesso, il monitoraggio non può più vederne la fine. Da chiamare prima del SaveChanges che salva il device.
    /// </summary>
    public static async Task CloseForDisabledDevicesAsync(VedettaVipDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var disabled = db.ChangeTracker.Entries<Device>()
            .Where(e => e.State == EntityState.Modified
                        && e.OriginalValues.GetValue<bool>(nameof(Device.Enabled)) && !e.Entity.Enabled)
            .Select(e => e.Entity.Id)
            .ToList();
        if (disabled.Count == 0)
            return;
        var outages = await db.Outages.Where(o => o.EndedAt == null && disabled.Contains(o.DeviceId)).ToListAsync(ct);
        foreach (var o in outages)
        {
            o.EndedAt = now < o.StartedAt ? o.StartedAt : now;
            o.EndNote = DisabledNote;
        }
    }

    public static OutageService ServiceOf(ThresholdKind kind) => kind switch
    {
        ThresholdKind.Latency => OutageService.Latency,
        ThresholdKind.Loss => OutageService.Loss,
        ThresholdKind.RouterOsCpu => OutageService.Cpu,
        ThresholdKind.RouterOsTemperature => OutageService.Temperature,
        _ => OutageService.Link
    };

    public static OutageService ServiceOf(RouterOsWatchKind kind) =>
        kind == RouterOsWatchKind.Interface ? OutageService.Interface : OutageService.WireGuard;

    private static string? Truncate(string? s, int max) => s is { Length: var n } && n > max ? s[..max] : s;
}
