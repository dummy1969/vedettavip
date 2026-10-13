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
/// Accesso remoto ai dispositivi (Impostazioni → Accesso remoto, pagina Gestore dei link):
/// <list type="bullet">
/// <item>percorsi di WinBox 4 e di PuTTY sui PC degli operatori, scritti negli installer del gestore dei link (null = l'installer
/// cerca nelle posizioni comuni e, se non trova il programma, chiede dove si trova); ammesse le variabili d'ambiente
/// (%LOCALAPPDATA%, %USERPROFILE%) su Windows e ~/ su Linux;</item>
/// <item>porte predefinite di WinBox, HTTP, HTTPS, SSH e Telnet per i dispositivi senza una propria (null = porta standard:
/// 8291, 80, 443, 22, 23).</item>
/// </list>
/// </summary>
public sealed record RemoteAccessSettingsDto(
    [property: StringLength(RemoteAccessSettingsDto.MaxPathLength, ErrorMessage = "Percorso di WinBox per Windows: al massimo 512 caratteri.")] string? WinBoxWindowsPath,
    [property: StringLength(RemoteAccessSettingsDto.MaxPathLength, ErrorMessage = "Percorso di WinBox per Linux: al massimo 512 caratteri.")] string? WinBoxLinuxPath,
    [property: Range(1, 65535, ErrorMessage = "Porta di WinBox: tra 1 e 65535.")] int? WinBoxPort = null,
    [property: StringLength(RemoteAccessSettingsDto.MaxPathLength, ErrorMessage = "Percorso di PuTTY per Windows: al massimo 512 caratteri.")] string? PuttyWindowsPath = null,
    [property: StringLength(RemoteAccessSettingsDto.MaxPathLength, ErrorMessage = "Percorso di PuTTY per Linux: al massimo 512 caratteri.")] string? PuttyLinuxPath = null,
    [property: Range(1, 65535, ErrorMessage = "Porta HTTP: tra 1 e 65535.")] int? HttpPort = null,
    [property: Range(1, 65535, ErrorMessage = "Porta HTTPS: tra 1 e 65535.")] int? HttpsPort = null,
    [property: Range(1, 65535, ErrorMessage = "Porta SSH: tra 1 e 65535.")] int? SshPort = null,
    [property: Range(1, 65535, ErrorMessage = "Porta Telnet: tra 1 e 65535.")] int? TelnetPort = null)
{
    public const int MaxPathLength = 512;
    public static readonly RemoteAccessSettingsDto Default = new(null, null);
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
