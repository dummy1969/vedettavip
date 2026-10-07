// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Shared.Models;

namespace VedettaVip.Api.Data.Entities;

/// <summary>
/// Stato corrente di un dispositivo, aggiornato dai report degli agenti. È stato derivato, non storico:
/// lo storico dei cambi è in <see cref="Event"/>. Per questo la FK verso Device è CASCADE.
/// </summary>
public sealed class DeviceStatus
{
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }
    public NodeState State { get; set; }
    /// <summary>Istante (orologio dell'agente) del passaggio allo stato corrente.</summary>
    public DateTimeOffset Since { get; set; }
    /// <summary>Istante (orologio dell'agente) dell'ultimo risultato applicato: scarta i risultati superati.</summary>
    public DateTimeOffset ObservedAt { get; set; }
    /// <summary>Istante (orologio dell'API) dell'ultimo report ricevuto: decide se lo stato è vecchio.</summary>
    public DateTimeOffset LastReportAt { get; set; }
    public double? LastRttMs { get; set; }
    public bool? SnmpOk { get; set; }
    public required string AgentId { get; set; }
}
