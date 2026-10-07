// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.SignalR;

namespace VedettaVip.Api.Hubs;

/// <summary>
/// Hub per gli agenti di polling, protetto da <see cref="Security.AgentKeyMiddleware"/>: il server invia
/// "TargetsChanged" quando cambiano i dispositivi. Nessun metodo invocabile dall'agente: i risultati
/// continuano a passare da POST /api/agent/status.
/// </summary>
public sealed class AgentHub : Hub;
