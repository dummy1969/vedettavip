// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;

namespace VedettaVip.Api.Data.Entities;

/// <summary>
/// Evento (cambio di stato di un device, agente offline/online). È anche l'outbox delle notifiche:
/// NotifyState = Pending e NotifyAfter scaduto → il dispatcher decide destinatari e dipendenze e crea le consegne.
/// </summary>
public sealed class Event
{
    public long Id { get; set; }
    /// <summary>Device dell'evento; null per gli eventi di un agente (AgentId).</summary>
    public Guid? DeviceId { get; set; }
    public Device? Device { get; set; }
    public string? AgentId { get; set; }
    /// <summary>Sempre in UTC (timestamptz).</summary>
    public DateTimeOffset Time { get; set; }
    public EventSeverity Severity { get; set; }
    public required string Type { get; set; }
    public required string Message { get; set; }
    public NodeState? FromState { get; set; }
    public NodeState? ToState { get; set; }
    public bool Acknowledged { get; set; }
    /// <summary>
    /// Ripristino che ha chiuso automaticamente il problema (device di nuovo Up, agente di nuovo online); null se
    /// ancora aperto o preso in carico a mano.
    /// </summary>
    public DateTimeOffset? ResolvedAt { get; set; }
    /// <summary>Utente che ha preso in carico l'evento (nome visualizzato) e quando; null se chiuso dal ripristino.</summary>
    public string? AcknowledgedBy { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }

    public NotifyState NotifyState { get; set; }
    /// <summary>Non prima di questo istante (orologio dell'API): ritardo per le dipendenze.</summary>
    public DateTimeOffset? NotifyAfter { get; set; }
    /// <summary>Esito sintetico (es. "soppresso: dipende da CORE, down", "nessun destinatario").</summary>
    public string? NotifyNote { get; set; }

    /// <summary>Per gli avvisi di soglia: identifica l'avviso ("latency", "loss", "link:{id}") su apertura e chiusura.</summary>
    public string? AlertKey { get; set; }
    /// <summary>Ultimo promemoria inviato per il problema ancora aperto, e quanti finora.</summary>
    public DateTimeOffset? LastReminderAt { get; set; }
    public int ReminderCount { get; set; }

    public List<NotificationDelivery> Deliveries { get; set; } = [];
}

/// <summary>Tipi di evento (colonna Type).</summary>
public static class EventTypes
{
    public const string StateChange = "StateChange";
    public const string AgentOffline = "AgentOffline";
    public const string AgentOnline = "AgentOnline";
    public const string ThresholdRaised = "ThresholdRaised";
    public const string ThresholdCleared = "ThresholdCleared";
}
