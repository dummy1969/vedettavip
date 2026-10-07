// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.SignalR;

namespace VedettaVip.Api.Hubs;

/// <summary>
/// Hub in sola lettura per il browser: il server invia "DeviceStateChanged" (DeviceStateChangedDto).
/// Nessun metodo invocabile dal client. Autenticazione da aggiungere (vedi roadmap).
/// </summary>
public sealed class StatusHub : Hub;
