// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

namespace VedettaVip.Api.Data.Entities;

/// <summary>Agente di polling (oggi VedettaVip.Worker) che invia report all'API.</summary>
public sealed class Agent
{
    public required string Id { get; set; }
    public DateTimeOffset FirstSeen { get; set; }
    /// <summary>Ultimo report ricevuto (cambi di stato o snapshot), orologio dell'API.</summary>
    public DateTimeOffset LastSeen { get; set; }
    /// <summary>Da quando l'agente è considerato offline (evento AgentOffline emesso); null = online.</summary>
    public DateTimeOffset? OfflineSince { get; set; }
}
