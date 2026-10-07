// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

namespace VedettaVip.Api.Data.Entities;

/// <summary>Cliente (scenario MSP): raggruppa i device per instradare le notifiche ai suoi destinatari.</summary>
public sealed class Customer
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Notes { get; set; }
    /// <summary>Profilo SNMP dei device del cliente che non ne hanno uno proprio; null = predefinito.</summary>
    public Guid? SnmpCredentialId { get; set; }
    public SnmpCredential? SnmpCredential { get; set; }
    /// <summary>Profilo RouterOS dei device del cliente che non ne hanno uno proprio; null = predefinito.</summary>
    public Guid? RouterOsCredentialId { get; set; }
    public RouterOsCredential? RouterOsCredential { get; set; }
}
