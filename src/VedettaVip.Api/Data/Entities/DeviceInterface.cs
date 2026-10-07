// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Data.Entities;

/// <summary>
/// Interfaccia di un device dall'ultimo inventario SNMP dell'agente. Dato derivato, sostituito a ogni
/// inventario: per questo la FK verso Device è CASCADE (come DeviceStatus).
/// </summary>
public sealed class DeviceInterface
{
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }
    public int IfIndex { get; set; }
    public string? Name { get; set; }
    public string? Alias { get; set; }
    public long? SpeedBps { get; set; }
    public InterfaceOperStatus OperStatus { get; set; }
    /// <summary>Istante (orologio dell'API) dell'inventario che ha prodotto la riga.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
