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
    /// <summary>Produttore: MikroTik abilita "Apri con WinBox". Con RouterOsApiEnabled è sempre MikroTik.</summary>
    public DeviceVendor Vendor { get; set; } = DeviceVendor.Generic;
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
    /// <summary>
    /// Notifiche disattivate: il device resta monitorato (stato, eventi, interruzioni) ma i suoi avvisi non partono
    /// (eventi soppressi dal dispatcher, niente promemoria).
    /// </summary>
    public bool NotificationsMuted { get; set; }
    /// <summary>Porta di WinBox per "Apri con WinBox"; null = quella generale (Impostazioni → WinBox, poi 8291).</summary>
    public int? WinBoxPort { get; set; }
    /// <summary>Accesso web di gestione (link http/https in una nuova scheda) e porta; null = generale del protocollo.</summary>
    public WebAccess WebAccess { get; set; } = WebAccess.None;
    public int? WebPort { get; set; }
    /// <summary>Accesso da terminale aperto con PuTTY (SSH o Telnet) e porta; null = generale del protocollo.</summary>
    public TerminalAccess TerminalAccess { get; set; } = TerminalAccess.None;
    public int? TerminalPort { get; set; }

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
