// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.EntityFrameworkCore;
using VedettaVip.Api.Data;
using VedettaVip.Api.Data.Entities;

namespace VedettaVip.Api.Services;

/// <summary>Finestre di manutenzione attive adesso, per mostrare i device in manutenzione (mappa, pagina Dispositivi).</summary>
public sealed class ActiveMaintenance(NotificationSettingsService settings, TimeProvider time)
{
    /// <summary>Funzione device → nome della finestra attiva che lo copre (null = nessuna). Carica tutto una volta.</summary>
    public async Task<Func<Guid, string?>> LoadAsync(VedettaVipDbContext db, CancellationToken ct)
    {
        var zone = settings.TimeZone(await settings.LoadAsync(db, ct));
        var now = time.GetUtcNow();
        var active = (await db.MaintenanceWindows.AsNoTracking().Where(w => w.Enabled).ToListAsync(ct))
            .Where(w => MaintenanceSchedule.IsActive(NotificationDispatcher.ToSchedule(w), now, zone))
            .ToList();
        if (active.Count == 0)
            return _ => null;

        var customers = await db.Devices.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.CustomerId, ct);
        var mapParents = await db.Maps.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.ParentMapId, ct);
        var deviceMaps = (await db.MapNodes.AsNoTracking().Where(n => n.DeviceId != null)
                .Select(n => new { DeviceId = n.DeviceId!.Value, n.MapId }).ToListAsync(ct))
            .ToLookup(x => x.DeviceId, x => x.MapId);

        return deviceId =>
        {
            var maps = new HashSet<Guid>();
            foreach (var map in deviceMaps[deviceId])
            {
                Guid? current = map;
                while (current is { } id && maps.Add(id))
                    current = mapParents.GetValueOrDefault(id);
            }
            var customer = customers.GetValueOrDefault(deviceId);
            return active.FirstOrDefault(w => MaintenanceSchedule.Covers(w.Scope, w.CustomerId, w.MapId, w.DeviceId, deviceId, customer, maps))?.Name;
        };
    }
}
