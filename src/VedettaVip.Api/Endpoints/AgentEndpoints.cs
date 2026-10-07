// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VedettaVip.Api.Data;
using VedettaVip.Api.Data.Entities;
using VedettaVip.Api.Hubs;
using VedettaVip.Api.Options;
using VedettaVip.Api.Security;
using VedettaVip.Api.Services;
using VedettaVip.Shared.Contracts;
using Npgsql;

namespace VedettaVip.Api.Endpoints;

public static class AgentEndpoints
{
    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder app)
    {
        // Endpoint usati dagli agenti di polling: protetti da X-Agent-Key
        var agent = app.MapGroup("/api/agent")
            .WithTags("Agent")
            .AddEndpointFilter<AgentKeyFilter>()
            .AllowAnonymous(); // nessun utente: protetti dalla chiave dell'agente

        agent.MapGet("/targets", GetTargetsAsync);
        agent.MapPost("/status", PostStatusAsync);
        agent.MapPost("/traffic", PostTrafficAsync);
        agent.MapPost("/interfaces", PostInterfacesAsync);
        agent.MapPost("/routeros", PostRouterOsAsync);
        agent.MapPost("/neighbors", DiscoveryEndpoints.PostNeighborsAsync);
        DiscoveryEndpoints.MapAgentScanEndpoints(agent);

        // Elenco agenti per la UI (banner "agente offline")
        app.MapGet("/api/agents", GetAgentsAsync).WithTags("Agents");

        // Traffico live per la mappa (ultimo campione per interfaccia)
        app.MapGet("/api/traffic", (TrafficCache cache) => TypedResults.Ok(cache.Current())).WithTags("Traffic");

        // Ultime letture RouterOS (CPU, memoria, temperatura...) per mappa e pagina Dispositivi
        app.MapGet("/api/routeros", (RouterOsCache cache) => TypedResults.Ok(cache.Current())).WithTags("RouterOs");

        return app;
    }

    private static async Task<Ok<List<AgentTargetDto>>> GetTargetsAsync(VedettaVipDbContext db, SecretProtector secrets, CancellationToken ct)
    {
        var devices = await db.Devices
            .AsNoTracking()
            .Where(d => d.Enabled)
            .OrderBy(d => d.Name)
            .Select(d => new { d.Id, d.Name, d.Address, d.SnmpVersion, d.DownAfterFailures, d.UpAfterSuccesses, d.SnmpDegradedAfterFailures,
                               d.SnmpCredentialId, d.CustomerId, d.RouterOsApiEnabled, d.RouterOsCredentialId, d.Type })
            .ToListAsync(ct);

        // Soglie effettive: quelle specifiche del device, altrimenti quelle generali
        var defaults = await SettingsEndpoints.LoadDetectionAsync(db, ct);

        // Interfacce da misurare: quelle usate come sorgente di traffico da almeno un link
        var interfaces = (await db.MapLinks
                .AsNoTracking()
                .Where(l => l.DeviceId != null && l.IfIndex != null)
                .Select(l => new { DeviceId = l.DeviceId!.Value, IfIndex = l.IfIndex!.Value })
                .Distinct()
                .ToListAsync(ct))
            .ToLookup(x => x.DeviceId, x => x.IfIndex);

        var community = await SnmpCredentialEndpoints.ResolveCommunitiesAsync(db, secrets, ct);
        var routerOs = await RouterOsCredentialEndpoints.ResolveAsync(db, secrets, ct);

        var targets = devices
            .Select(d => new AgentTargetDto(d.Id, d.Name, d.Address, d.SnmpVersion, interfaces[d.Id].Order().ToList(),
                defaults.WithOverrides(d.DownAfterFailures, d.UpAfterSuccesses, d.SnmpDegradedAfterFailures),
                d.SnmpVersion is SnmpVersion.V1 or SnmpVersion.V2c ? community(d.SnmpCredentialId, d.CustomerId) : null,
                d.RouterOsApiEnabled ? routerOs(d.RouterOsCredentialId, d.CustomerId) : null,
                d.Type is DeviceType.Router or DeviceType.Firewall))
            .ToList();

        return TypedResults.Ok(targets);
    }

    private static async Task<Ok<AgentStatusAckDto>> PostStatusAsync(
        AgentStatusReportDto report, DeviceStatusService service, CancellationToken ct) =>
        TypedResults.Ok(await service.ProcessAsync(report, ct));

    /// <summary>Aggiorna la cache del traffico live e inoltra ai browser i campioni nuovi ("TrafficUpdated").</summary>
    private static async Task<NoContent> PostTrafficAsync(
        AgentTrafficReportDto report, TrafficCache cache, IHubContext<StatusHub> hub, MetricWriter metrics,
        ILoggerFactory loggers, CancellationToken ct)
    {
        var updated = cache.Update(report.Samples);
        if (updated.Count > 0)
            await hub.Clients.All.SendAsync(StatusHubMessages.TrafficUpdated, updated, ct);

        // Lo storico non deve bloccare il dato live: se il database non risponde si perde solo questo campione
        try
        {
            await metrics.WriteAsync(updated, report.Pings ?? [], ct);
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
        {
            loggers.CreateLogger(nameof(AgentEndpoints)).LogWarning(ex, "Metriche non salvate ({Traffic} campioni di traffico, {Pings} ping)",
                updated.Count, report.Pings?.Count ?? 0);
        }

        return TypedResults.NoContent();
    }

    /// <summary>Letture RouterOS: cache live, inoltro ai browser ("RouterOsUpdated") e storico in Metrics.</summary>
    private static async Task<NoContent> PostRouterOsAsync(
        AgentRouterOsReportDto report, RouterOsCache cache, IHubContext<StatusHub> hub, MetricWriter metrics,
        RouterOsWatchService watches, ILoggerFactory loggers, CancellationToken ct)
    {
        var updated = cache.Update(report.Samples);

        // Interfacce e tunnel sorvegliati: avvisi e numero di problemi per la colonna RouterOS
        try
        {
            var okIds = updated.Where(s => s.Ok).Select(s => s.DeviceId).ToList();
            cache.SetWatchProblems(okIds, await watches.ProcessAsync(updated, ct));
        }
        catch (Exception ex) when (ex is NpgsqlException or DbUpdateException or TimeoutException)
        {
            loggers.CreateLogger(nameof(AgentEndpoints)).LogWarning(ex, "Sorveglianza di interfacce e tunnel non valutata");
        }

        if (updated.Count > 0)
            await hub.Clients.All.SendAsync(StatusHubMessages.RouterOsUpdated, updated.Select(cache.Summary).ToList(), ct);

        try
        {
            await metrics.WriteRouterOsAsync(updated, ct);
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
        {
            loggers.CreateLogger(nameof(AgentEndpoints)).LogWarning(ex, "Metriche RouterOS non salvate ({Count} letture)", updated.Count);
        }
        return TypedResults.NoContent();
    }

    /// <summary>Sostituisce l'inventario delle interfacce del device (delete + insert nella stessa transazione).</summary>
    private static async Task<Results<NoContent, NotFound>> PostInterfacesAsync(
        AgentInterfacesReportDto report, VedettaVipDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (!await db.Devices.AnyAsync(d => d.Id == report.DeviceId, ct))
            return TypedResults.NotFound(); // device eliminato nel frattempo

        var now = time.GetUtcNow();
        var rows = report.Interfaces
            .DistinctBy(i => i.IfIndex)
            .Select(i => new DeviceInterface
            {
                DeviceId = report.DeviceId,
                IfIndex = i.IfIndex,
                Name = i.Name,
                Alias = string.IsNullOrWhiteSpace(i.Alias) ? null : i.Alias,
                SpeedBps = i.SpeedBps,
                OperStatus = i.OperStatus,
                UpdatedAt = now
            });

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.DeviceInterfaces.Where(i => i.DeviceId == report.DeviceId).ExecuteDeleteAsync(ct);
        db.DeviceInterfaces.AddRange(rows);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return TypedResults.NoContent();
    }

    private static async Task<Ok<List<AgentDto>>> GetAgentsAsync(
        VedettaVipDbContext db, IOptions<AgentOptions> options, TimeProvider time, CancellationToken ct)
    {
        var onlineAfter = time.GetUtcNow() - options.Value.StaleAfter;
        var agents = await db.Agents
            .AsNoTracking()
            .OrderBy(a => a.Id)
            .Select(a => new AgentDto(a.Id, a.LastSeen, a.LastSeen >= onlineAfter))
            .ToListAsync(ct);

        return TypedResults.Ok(agents);
    }
}
