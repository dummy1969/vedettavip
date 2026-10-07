// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

namespace VedettaVip.Api.Data.Entities;

/// <summary>
/// Profilo di credenziali SNMP v1/v2c condiviso (es. uno per cliente). La community è cifrata con Data Protection
/// (<see cref="Services.SecretProtector"/>) e non torna mai alla UI: viene decifrata solo per gli agenti.
/// Profilo effettivo di un device: il suo, altrimenti quello del cliente, altrimenti il predefinito
/// (MonitoringSettings.DefaultSnmpCredentialId), altrimenti la community della configurazione del Worker.
/// </summary>
public sealed class SnmpCredential
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string CommunityProtected { get; set; }
}
