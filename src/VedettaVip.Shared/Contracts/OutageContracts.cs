// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

namespace VedettaVip.Shared.Contracts;

/// <summary>
/// Servizio coinvolto in un'interruzione: Ping = device Down, Snmp = device Partial, Interface/WireGuard = elementi
/// RouterOS sorvegliati, gli altri sono le soglie sulle metriche (Link = utilizzo di un link).
/// </summary>
public enum OutageService { Ping, Snmp, Interface, WireGuard, Latency, Loss, Cpu, Temperature, Link }

/// <summary>Filtro sullo stato: tutte, solo quelle in corso, solo quelle finite.</summary>
public enum OutageStatusFilter { All, Active, Resolved }

/// <summary>
/// Interruzione: intervallo in cui un servizio del device è stato giù (EndedAt null = ancora in corso). Indipendente dalla
/// presa in carico degli eventi: si chiude solo al ripristino (o alla disattivazione del monitoraggio, con EndNote).
/// </summary>
public sealed record OutageDto(
    Guid Id,
    Guid DeviceId,
    string DeviceName,
    string DeviceAddress,
    string? CustomerName,
    OutageService Service,
    string Key,
    string? Detail,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string? EndNote,
    string? Maintenance); // finestra di manutenzione attiva all'inizio o alla fine; null = fuori manutenzione

/// <summary>
/// Riepilogo di un device su un periodo (GET /api/devices/{id}/availability): interruzioni di qualsiasi servizio che
/// toccano il periodo e disponibilità calcolata sul ping, esclusi i tempi in manutenzione.
/// </summary>
public sealed record OutageSummaryDto(
    Guid DeviceId,
    DateTimeOffset From,
    DateTimeOffset To,
    int Outages,
    int Active,
    int PingOutages,
    long DownSeconds,
    long MaintenanceDownSeconds,
    double AvailabilityPct);

/// <summary>Esito di "Rimuovi risolte".</summary>
public sealed record OutageDeleteResultDto(int Deleted);
