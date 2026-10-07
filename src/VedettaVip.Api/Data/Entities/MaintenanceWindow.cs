// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Data.Entities;

/// <summary>
/// Finestra di manutenzione (singola o settimanale) su tutto, un cliente, una mappa (con sottomappe) o un device:
/// gli eventi dei device coinvolti vengono registrati ma non notificati finché è attiva.
/// </summary>
public sealed class MaintenanceWindow
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public MaintenanceScope Scope { get; set; }
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public Guid? MapId { get; set; }
    public Map? Map { get; set; }
    public Guid? DeviceId { get; set; }
    public Device? Device { get; set; }
    public MaintenanceRecurrence Recurrence { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    /// <summary>Bit 0 = domenica … bit 6 = sabato (DayOfWeek).</summary>
    public int DaysOfWeek { get; set; }
    /// <summary>Ora locale di inizio (fuso orario delle notifiche).</summary>
    public TimeOnly? StartTime { get; set; }
    public int DurationMinutes { get; set; }
    public bool Enabled { get; set; } = true;
    public string? Notes { get; set; }
}
