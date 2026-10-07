// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Services;

/// <summary>Calcolo delle finestre di manutenzione (puro, coperto da test).</summary>
public static class MaintenanceSchedule
{
    /// <summary>Definizione minima di una finestra per il calcolo (dall'entità o dal DTO).</summary>
    public sealed record Window(
        MaintenanceRecurrence Recurrence, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt,
        int DaysOfWeek, TimeOnly? StartTime, int DurationMinutes, bool Enabled);

    /// <summary>La finestra è attiva nell'istante dato? Le settimanali usano l'ora locale del fuso (ora legale compresa).</summary>
    public static bool IsActive(Window w, DateTimeOffset at, TimeZoneInfo zone)
    {
        if (!w.Enabled)
            return false;

        if (w.Recurrence == MaintenanceRecurrence.Once)
            return w.StartsAt is { } s && w.EndsAt is { } e && at >= s && at < e;

        // Settimanale: si controllano le occorrenze iniziate oggi e nei giorni precedenti che coprono la durata
        if (w.StartTime is not { } startTime || w.DurationMinutes <= 0 || w.DaysOfWeek == 0)
            return false;

        var local = TimeZoneInfo.ConvertTime(at, zone);
        var daysBack = (int)Math.Ceiling(w.DurationMinutes / 1440.0);
        for (var back = 0; back <= daysBack; back++)
        {
            var day = DateOnly.FromDateTime(local.DateTime).AddDays(-back);
            if ((w.DaysOfWeek & (1 << (int)day.DayOfWeek)) == 0)
                continue;
            var start = ToInstant(day.ToDateTime(startTime), zone);
            if (at >= start && at < start.AddMinutes(w.DurationMinutes))
                return true;
        }
        return false;
    }

    /// <summary>Prossimo inizio dopo l'istante dato (per la UI); null se non ce ne sono.</summary>
    public static DateTimeOffset? NextStart(Window w, DateTimeOffset after, TimeZoneInfo zone)
    {
        if (!w.Enabled)
            return null;
        if (w.Recurrence == MaintenanceRecurrence.Once)
            return w.StartsAt is { } s && s > after ? s : null;
        if (w.StartTime is not { } startTime || w.DaysOfWeek == 0)
            return null;

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(after, zone).DateTime);
        for (var ahead = 0; ahead <= 7; ahead++)
        {
            var day = today.AddDays(ahead);
            if ((w.DaysOfWeek & (1 << (int)day.DayOfWeek)) == 0)
                continue;
            var start = ToInstant(day.ToDateTime(startTime), zone);
            if (start > after)
                return start;
        }
        return null;
    }

    /// <summary>
    /// La finestra copre il device? Tutto; il suo cliente; una mappa che lo contiene o che contiene (anche
    /// indirettamente) la sua sottomappa (<paramref name="mapsWithAncestors"/>); il device stesso.
    /// </summary>
    public static bool Covers(MaintenanceScope scope, Guid? customerId, Guid? mapId, Guid? deviceId,
        Guid device, Guid? deviceCustomerId, IReadOnlySet<Guid> mapsWithAncestors) => scope switch
    {
        MaintenanceScope.All => true,
        MaintenanceScope.Customer => customerId is not null && customerId == deviceCustomerId,
        MaintenanceScope.Map => mapId is { } m && mapsWithAncestors.Contains(m),
        _ => deviceId == device
    };

    /// <summary>Ora locale → istante. Ora inesistente (salto avanti dell'ora legale): si sposta in avanti di un'ora.</summary>
    private static DateTimeOffset ToInstant(DateTime local, TimeZoneInfo zone)
    {
        if (zone.IsInvalidTime(local))
            local = local.AddHours(1);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
