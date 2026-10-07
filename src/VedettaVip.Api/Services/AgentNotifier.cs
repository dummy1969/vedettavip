// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.SignalR;
using VedettaVip.Api.Hubs;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Services;

/// <summary>
/// Avvisa gli agenti collegati che l'elenco dei target è cambiato. Una notifica persa non è grave:
/// gli agenti ricaricano comunque i target periodicamente e dopo ogni riconnessione all'hub.
/// </summary>
public sealed class AgentNotifier(IHubContext<AgentHub> hub, ILogger<AgentNotifier> logger)
{
    /// <summary>Chiede agli agenti di rileggere subito l'elenco delle interfacce del device.</summary>
    public async Task InterfacesRequestedAsync(Guid deviceId)
    {
        try
        {
            await hub.Clients.All.SendAsync(AgentHubMessages.InterfacesRequested, deviceId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Richiesta di inventario interfacce per {DeviceId} non inviata", deviceId);
        }
    }

    /// <summary>Chiede agli agenti di rileggere subito i vicini di tutti i device ("Scopri ora").</summary>
    public async Task NeighborsRequestedAsync()
    {
        try
        {
            await hub.Clients.All.SendAsync(AgentHubMessages.NeighborsRequested, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Richiesta di lettura dei vicini non inviata");
        }
    }

    /// <summary>Chiede agli agenti di eseguire la scansione (il primo che la legge la prende).</summary>
    public async Task ScanRequestedAsync(Guid scanId)
    {
        try
        {
            await hub.Clients.All.SendAsync(AgentHubMessages.ScanRequested, scanId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Richiesta di scansione {ScanId} non inviata", scanId);
        }
    }

    public async Task TargetsChangedAsync()
    {
        try
        {
            // Non legato alla richiesta HTTP: la modifica è già salvata, la notifica deve partire comunque
            await hub.Clients.All.SendAsync(AgentHubMessages.TargetsChanged, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Notifica TargetsChanged agli agenti non inviata");
        }
    }
}
