// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.ComponentModel.DataAnnotations;
using VedettaVip.Shared.Models;

namespace VedettaVip.Shared.Contracts;

public sealed record DeviceDto(
    Guid Id,
    string Name,
    string Address,
    DeviceType Type,
    string? Icon,
    SnmpVersion SnmpVersion,
    Guid? SnmpCredentialId,
    bool RouterOsApiEnabled,
    Guid? ParentDeviceId,
    bool Enabled,
    int? DownAfterFailures = null,
    int? UpAfterSuccesses = null,
    int? SnmpDegradedAfterFailures = null,
    Guid? CustomerId = null,
    int? RttThresholdMs = null,
    double? LossThresholdPct = null,
    Guid? RouterOsCredentialId = null,
    int? CpuThresholdPct = null,
    int? TemperatureThresholdC = null,
    DeviceVendor Vendor = DeviceVendor.Generic);

/// <summary>
/// Creazione/modifica di un device. Le tre soglie di rilevazione sono facoltative: null = valore generale
/// (pagina Impostazioni). Attenzione: il PUT sostituisce tutto, anche le soglie.
/// </summary>
public sealed record DeviceUpsertDto(
    [property: Required(ErrorMessage = "Il nome è obbligatorio."), StringLength(128, ErrorMessage = "Il nome supera i 128 caratteri.")] string Name,
    [property: Required(ErrorMessage = "L'indirizzo è obbligatorio."), StringLength(255, ErrorMessage = "L'indirizzo supera i 255 caratteri.")] string Address,
    DeviceType Type = DeviceType.Other,
    [property: StringLength(64)] string? Icon = null,
    SnmpVersion SnmpVersion = SnmpVersion.None,
    Guid? SnmpCredentialId = null,
    bool RouterOsApiEnabled = false,
    Guid? ParentDeviceId = null,
    bool Enabled = true,
    [property: Range(1, 100, ErrorMessage = "Ping persi per Down: tra 1 e 100.")] int? DownAfterFailures = null,
    [property: Range(1, 100, ErrorMessage = "Ping riusciti per Up: tra 1 e 100.")] int? UpAfterSuccesses = null,
    [property: Range(1, 100, ErrorMessage = "Errori SNMP per Partial: tra 1 e 100.")] int? SnmpDegradedAfterFailures = null,
    Guid? CustomerId = null,
    // Soglie sulle metriche: null = generale, 0 = disattivata
    [property: Range(0, 100_000, ErrorMessage = "Latenza: tra 0 (disattivata) e 100000 ms.")] int? RttThresholdMs = null,
    [property: Range(0, 100, ErrorMessage = "Perdita: tra 0 (disattivata) e 100 %.")] double? LossThresholdPct = null,
    Guid? RouterOsCredentialId = null,
    [property: Range(0, 100, ErrorMessage = "CPU: tra 0 (disattivata) e 100 %.")] int? CpuThresholdPct = null,
    [property: Range(0, 150, ErrorMessage = "Temperatura: tra 0 (disattivata) e 150 °C.")] int? TemperatureThresholdC = null,
    /// <summary>Produttore; con l'API RouterOS abilitata l'API lo porta comunque a MikroTik.</summary>
    DeviceVendor Vendor = DeviceVendor.Generic);

/// <summary>Corpo della risposta 409 quando si tenta di eliminare un dispositivo ancora in uso.</summary>
public sealed record DeviceInUseDto(
    Guid DeviceId,
    string Message,
    IReadOnlyList<MapRefDto> Maps,
    int EventCount);

/// <summary>
/// Riga della pagina dispositivi: anagrafica, stato corrente (Unknown se disabilitato o se l'ultimo report
/// è vecchio, come sulla mappa) e mappe in cui il dispositivo compare come nodo.
/// </summary>
public sealed record DeviceOverviewDto(
    DeviceDto Device,
    NodeState State,
    DateTimeOffset? Since,
    DateTimeOffset? LastReportAt,
    double? LastRttMs,
    bool? SnmpOk,
    string? AgentId,
    IReadOnlyList<MapRefDto> Maps,
    /// <summary>Nome della finestra di manutenzione attiva che copre il device (null = nessuna).</summary>
    string? Maintenance = null);
