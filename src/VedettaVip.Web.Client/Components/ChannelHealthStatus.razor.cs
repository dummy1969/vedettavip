// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Web.Client.Components;

/// <summary>Stato di un canale di notifica (GET /api/notifications/health) nella card delle Impostazioni.</summary>
public partial class ChannelHealthStatus
{
    /// <summary>Null se il canale non è attivo: non si mostra nulla.</summary>
    [Parameter] public NotificationChannelHealthDto? Health { get; set; }
}
