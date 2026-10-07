// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.Extensions.Options;
using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;
using VedettaVip.Worker.Api;
using VedettaVip.Worker.Options;
using VedettaVip.Worker.Probes;
using VedettaVip.Worker.State;

namespace VedettaVip.Worker.Services;

/// <summary>
/// Ogni Polling:RouterOsIntervalSeconds legge risorse e salute dei target con accesso RouterOS (API abilitata e profilo
/// effettivo, deciso dall'API). Salta i device Down (sarebbero solo timeout). Errori di connessione o di login diventano
/// letture con Ok = false e il motivo, visibile nella pagina Dispositivi: non cambiano lo stato del device.
/// </summary>
public sealed class RouterOsPollingService(
    TargetProvider targets,
    DeviceStateStore states,
    AgentApiClient api,
    IOptions<PollingOptions> options,
    IOptions<AgentOptions> agentOptions,
    TimeProvider time,
    ILogger<RouterOsPollingService> logger) : BackgroundService
{
    /// <summary>Ultimo errore per device: lo si logga solo quando cambia, non a ogni ciclo.</summary>
    private readonly ConcurrentDictionary<Guid, string> lastError = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(o.RouterOsIntervalSeconds), time);
        do
        {
            var list = targets.Current?.Where(t => t.RouterOs is not null && states.StateOf(t.DeviceId) != NodeState.Down).ToList();
            if (list is not { Count: > 0 })
                continue;

            var samples = new ConcurrentBag<RouterOsSampleDto>();
            var parallel = new ParallelOptions { MaxDegreeOfParallelism = o.MaxDegreeOfParallelism, CancellationToken = stoppingToken };
            try
            {
                await Parallel.ForEachAsync(list, parallel, async (target, ct) => samples.Add(await ReadAsync(target, o, ct)));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            await api.SendRouterOsAsync(new AgentRouterOsReportDto(agentOptions.Value.AgentId, [.. samples]), stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<RouterOsSampleDto> ReadAsync(AgentTargetDto target, PollingOptions o, CancellationToken ct)
    {
        var ros = target.RouterOs!;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(o.RouterOsTimeoutMs);
        var service = RouterOsErrors.Service(ros.UseTls, ros.Port);
        string error;
        try
        {
            await using var client = await RouterOsApiClient.ConnectAsync(
                target.Address, ros.Port, ros.UseTls, ros.VerifyCertificate, ros.Username, ros.Password, timeout.Token);
            var sample = await RouterOsReader.ReadAsync(client, target.DeviceId, time.GetUtcNow(), timeout.Token);
            if (lastError.TryRemove(target.DeviceId, out _))
                logger.LogInformation("{Name}: API RouterOS di nuovo raggiungibile ({Service})", target.Name, service);
            return sample with { Service = service };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            error = $"timeout ({o.RouterOsTimeoutMs / 1000.0:0.#} s) su {service}: router lento o pacchetti filtrati";
        }
        catch (Exception ex) when (ex is RouterOsApiException or SocketException or AuthenticationException or IOException)
        {
            error = RouterOsErrors.Describe(ex, ros.UseTls, ros.Port);
            logger.LogDebug(ex, "{Name}: dettaglio dell'errore RouterOS", target.Name);
        }

        if (lastError.TryGetValue(target.DeviceId, out var previous) is false || previous != error)
            logger.LogWarning("{Name} ({Address}): API RouterOS non letta: {Error}", target.Name, target.Address, error);
        lastError[target.DeviceId] = error;
        return new RouterOsSampleDto(target.DeviceId, time.GetUtcNow(), Ok: false, Error: error[..Math.Min(error.Length, 512)], Service: service);
    }
}
