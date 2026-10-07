// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Data.Entities;

public sealed class Device
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    /// <summary>Indirizzo IP o hostname.</summary>
    public required string Address { get; set; }
    public DeviceType Type { get; set; } = DeviceType.Other;
    public string? Icon { get; set; }
    public SnmpVersion SnmpVersion { get; set; } = SnmpVersion.None;
    /// <summary>Profilo di credenziali SNMP; null = quello del cliente o il predefinito.</summary>
    public Guid? SnmpCredentialId { get; set; }
    public SnmpCredential? SnmpCredential { get; set; }
    public bool RouterOsApiEnabled { get; set; }
    /// <summary>Profilo di accesso all'API RouterOS; null = quello del cliente o il predefinito.</summary>
    public Guid? RouterOsCredentialId { get; set; }
    public RouterOsCredential? RouterOsCredential { get; set; }
    /// <summary>Dipendenza per la soppressione degli alert.</summary>
    public Guid? ParentDeviceId { get; set; }
    public Device? ParentDevice { get; set; }
    public bool Enabled { get; set; } = true;

    // Soglie di rilevazione specifiche del device; null = valore generale (MonitoringSettings)
    public int? DownAfterFailures { get; set; }
    public int? UpAfterSuccesses { get; set; }
    public int? SnmpDegradedAfterFailures { get; set; }

    // Soglie sulle metriche specifiche del device; null = generale, 0 = disattivata
    public int? RttThresholdMs { get; set; }
    public double? LossThresholdPct { get; set; }
    // Soglie RouterOS specifiche; null = generale, 0 = disattivata
    public int? CpuThresholdPct { get; set; }
    public int? TemperatureThresholdC { get; set; }
    public DeviceStatus? Status { get; set; }

    /// <summary>Cliente del device (instradamento delle notifiche); null = nessuno.</summary>
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }
}
