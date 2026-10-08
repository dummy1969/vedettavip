// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.ComponentModel.DataAnnotations;

namespace VedettaVip.Shared.Contracts;

/// <summary>
/// Soglie di rilevazione dello stato (isteresi), in numero di poll consecutivi (un poll ogni 30 s):
/// Down dopo DownAfterFailures ping persi, di nuovo Up dopo UpAfterSuccesses ping riusciti,
/// Partial dopo SnmpDegradedAfterFailures errori SNMP con ping ok.
/// </summary>
public sealed record DetectionThresholdsDto(
    [property: Range(1, 100, ErrorMessage = "Ping persi per Down: tra 1 e 100.")] int DownAfterFailures,
    [property: Range(1, 100, ErrorMessage = "Ping riusciti per Up: tra 1 e 100.")] int UpAfterSuccesses,
    [property: Range(1, 100, ErrorMessage = "Errori SNMP per Partial: tra 1 e 100.")] int SnmpDegradedAfterFailures)
{
    /// <summary>Valori iniziali (gli stessi di prima che fossero configurabili).</summary>
    public static readonly DetectionThresholdsDto Default = new(3, 2, 2);

    /// <summary>Soglie effettive di un device: i valori specifici, dove presenti, sostituiscono quelli generali.</summary>
    public DetectionThresholdsDto WithOverrides(int? downAfterFailures, int? upAfterSuccesses, int? snmpDegradedAfterFailures) =>
        new(downAfterFailures ?? DownAfterFailures, upAfterSuccesses ?? UpAfterSuccesses, snmpDegradedAfterFailures ?? SnmpDegradedAfterFailures);
}

/// <summary>
/// Soglie generali sulle metriche, valutate come media sulla finestra (minuti). Null = soglia disattivata.
/// L'avviso si apre sopra la soglia e si chiude sotto l'80% (isteresi). Device e link possono sovrascriverle
/// (null = generale, 0 = disattivata).
/// </summary>
public sealed record MetricThresholdsDto(
    [property: Range(1, 100_000, ErrorMessage = "Latenza: tra 1 e 100000 ms.")] int? RttMs,
    [property: Range(0.1, 100, ErrorMessage = "Perdita: tra 0,1 e 100 %.")] double? LossPct,
    [property: Range(1, 100, ErrorMessage = "Utilizzo link: tra 1 e 100 %.")] int? LinkUtilizationPct,
    [property: Range(1, 60, ErrorMessage = "Finestra: tra 1 e 60 minuti.")] int WindowMinutes,
    [property: Range(1, 100, ErrorMessage = "CPU RouterOS: tra 1 e 100 %.")] int? CpuPct = null,
    [property: Range(1, 150, ErrorMessage = "Temperatura RouterOS: tra 1 e 150 °C.")] int? TemperatureC = null)
{
    public static readonly MetricThresholdsDto Default = new(100, 5, 80, 5, 90, 75);
}

/// <summary>Impostazioni della dashboard: ore di eventi recenti mostrate (default 4, massimo una settimana).</summary>
public sealed record DashboardSettingsDto(
    [property: Range(1, DashboardSettingsDto.MaxEventHours, ErrorMessage = "Eventi recenti: tra 1 e 168 ore.")] int RecentEventHours)
{
    public const int MaxEventHours = 168;
    public static readonly DashboardSettingsDto Default = new(4);
}

/// <summary>
/// Percorso di WinBox 4 sui PC degli operatori, scritto negli installer del gestore dei link winbox:// (pagina WinBox).
/// Null = nessun predefinito: l'installer cerca nelle posizioni comuni e, se non lo trova, chiede il percorso.
/// Ammesse le variabili d'ambiente (%LOCALAPPDATA%, %USERPROFILE%) su Windows e ~/ su Linux.
/// </summary>
public sealed record WinBoxSettingsDto(
    [property: StringLength(WinBoxSettingsDto.MaxPathLength, ErrorMessage = "Percorso Windows: al massimo 512 caratteri.")] string? WindowsPath,
    [property: StringLength(WinBoxSettingsDto.MaxPathLength, ErrorMessage = "Percorso Linux: al massimo 512 caratteri.")] string? LinuxPath)
{
    public const int MaxPathLength = 512;
    public static readonly WinBoxSettingsDto Default = new(null, null);
}

public enum MaintenanceScope { All, Customer, Map, Device }

public enum MaintenanceRecurrence { Once, Weekly }

/// <summary>
/// Finestra di manutenzione: durante la finestra gli eventi dei device coinvolti sono registrati ma non notificati.
/// Once: StartsAt-EndsAt. Weekly: giorni (bit 0 = domenica … bit 6 = sabato), ora di inizio locale (fuso delle
/// notifiche) e durata; può scavalcare la mezzanotte.
/// </summary>
public sealed record MaintenanceWindowDto(
    Guid Id,
    string Name,
    MaintenanceScope Scope,
    Guid? CustomerId,
    Guid? MapId,
    Guid? DeviceId,
    string? ScopeName,
    MaintenanceRecurrence Recurrence,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    int DaysOfWeek,
    TimeOnly? StartTime,
    int DurationMinutes,
    bool Enabled,
    string? Notes,
    bool ActiveNow,
    DateTimeOffset? NextStart);

public sealed record MaintenanceWindowUpsertDto(
    [property: Required(ErrorMessage = "Il nome è obbligatorio."), StringLength(128)] string Name,
    MaintenanceScope Scope,
    Guid? CustomerId,
    Guid? MapId,
    Guid? DeviceId,
    MaintenanceRecurrence Recurrence,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    [property: Range(0, 127)] int DaysOfWeek,
    TimeOnly? StartTime,
    [property: Range(0, 10_080, ErrorMessage = "Durata: al massimo una settimana.")] int DurationMinutes,
    bool Enabled = true,
    [property: StringLength(1024)] string? Notes = null);
