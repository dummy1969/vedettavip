// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;
using VedettaVip.Shared.Contracts;
using VedettaVip.Worker.Options;

namespace VedettaVip.Worker.Services;

/// <summary>
/// Resta collegato all'hub degli agenti (/hubs/agent, header X-Agent-Key) e su "TargetsChanged" anticipa il
/// refresh dei target. Dopo ogni (ri)connessione chiede comunque un refresh: le notifiche arrivate mentre
/// era scollegato sono perse. Se l'hub non è raggiungibile resta il refresh periodico del TargetProvider.
/// </summary>
public sealed class AgentHubListener(
    TargetProvider targets,
    InterfaceInventoryService inventory,
    NeighborDiscoveryService discovery,
    SubnetScanService scans,
    IOptions<AgentOptions> options,
    ILogger<AgentHubListener> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var agent = options.Value;
        var url = new Uri(new Uri(agent.ApiBaseUrl.TrimEnd('/') + "/"), AgentHubMessages.HubPath);

        await using var hub = new HubConnectionBuilder()
            .WithUrl(url, o => o.Headers[AgentHeaders.ApiKey] = agent.ApiKey)
            .WithAutomaticReconnect(new ForeverRetryPolicy())
            .Build();

        hub.On(AgentHubMessages.TargetsChanged, () =>
        {
            logger.LogDebug("Notifica TargetsChanged ricevuta");
            targets.RequestRefresh();
        });

        hub.On<Guid>(AgentHubMessages.InterfacesRequested, deviceId =>
        {
            logger.LogDebug("Richiesto inventario interfacce di {DeviceId}", deviceId);
            inventory.Request(deviceId);
        });

        hub.On(AgentHubMessages.NeighborsRequested, () =>
        {
            logger.LogInformation("Richiesta la lettura dei vicini di tutti i dispositivi (Scopri ora)");
            discovery.RequestAll();
        });

        hub.On<Guid>(AgentHubMessages.ScanRequested, scanId =>
        {
            logger.LogInformation("Richiesta la scansione {ScanId}", scanId);
            scans.Request(scanId);
        });

        hub.Reconnecting += ex =>
        {
            logger.LogWarning("Connessione all'hub agent persa ({Message}): riconnessione in corso", ex?.Message);
            return Task.CompletedTask;
        };
        hub.Reconnected += _ =>
        {
            logger.LogInformation("Riconnesso all'hub agent");
            targets.RequestRefresh();
            return Task.CompletedTask;
        };

        // La prima connessione non è coperta da WithAutomaticReconnect: riprova finché l'API non risponde.
        // Solo il primo fallimento è un warning, per non riempire il log quando l'API è giù a lungo.
        var policy = new ForeverRetryPolicy();
        for (var attempt = 0; !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                await hub.StartAsync(stoppingToken);
                logger.LogInformation("Collegato all'hub agent {Url}: i nuovi dispositivi vengono interrogati subito", url);
                targets.RequestRefresh();
                break;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                logger.LogError("Hub agent: chiave rifiutata (401), verificare Agent:ApiKey su API e Worker");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (attempt == 0)
                    logger.LogWarning("Hub agent non raggiungibile ({Message}): uso il refresh periodico dei target e riprovo", ex.Message);
                else
                    logger.LogDebug("Hub agent ancora non raggiungibile: {Message}", ex.Message);
            }

            await Task.Delay(policy.Delay(attempt), stoppingToken);
        }

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Riconnessione senza limite di tentativi: 0, 2, 5, 10 s, poi ogni 30 s.</summary>
    private sealed class ForeverRetryPolicy : IRetryPolicy
    {
        private static readonly TimeSpan[] Delays =
            [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];

        public TimeSpan Delay(long attempt) => attempt < Delays.Length ? Delays[attempt] : TimeSpan.FromSeconds(30);

        public TimeSpan? NextRetryDelay(RetryContext retryContext) => Delay(retryContext.PreviousRetryCount);
    }
}
