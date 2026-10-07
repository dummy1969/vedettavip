// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Collections.Concurrent;
using System.Net;
using Lextm.SharpSnmpLib;
using Microsoft.Extensions.Options;
using VedettaVip.Shared.Contracts;
using VedettaVip.Worker.Api;
using VedettaVip.Worker.Options;
using VedettaVip.Worker.Probes;

namespace VedettaVip.Worker.Services;

/// <summary>
/// Inventario delle interfacce dei device SNMP v2c (ifName, ifAlias, ifHighSpeed, ifOperStatus), inviato a
/// POST /api/agent/interfaces: serve alla UI per scegliere l'interfaccia di un link per nome.
/// Un device viene letto: al primo avvistamento, quando cambiano indirizzo o versione SNMP, ogni
/// Polling:InterfaceInventoryMinutes e subito su richiesta (<see cref="Request"/>, dall'hub agent).
/// Dopo un errore si riprova tra 5 minuti, per non insistere su device spenti o senza SNMP.
/// </summary>
public sealed class InterfaceInventoryService(
    TargetProvider targets,
    IcmpProbe icmp,
    SnmpProbe snmp,
    AgentApiClient api,
    TimeProvider time,
    IOptions<PollingOptions> options,
    IOptions<AgentOptions> agentOptions,
    ILogger<InterfaceInventoryService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryAfterError = TimeSpan.FromMinutes(5);
    private const int MaxParallel = 4;

    private const string IfName = "1.3.6.1.2.1.31.1.1.1.1";
    private const string IfAlias = "1.3.6.1.2.1.31.1.1.1.18";
    private const string IfHighSpeed = "1.3.6.1.2.1.31.1.1.1.15";
    private const string IfOperStatus = "1.3.6.1.2.1.2.2.1.8";
    // MIB-II ifTable: ripiego per gli apparati senza ifXTable (es. DrayTek Vigor con firmware datati)
    private const string IfDescr = "1.3.6.1.2.1.2.2.1.2";
    private const string IfSpeed = "1.3.6.1.2.1.2.2.1.5";

    /// <summary>Prossimo inventario per device; Signature = indirizzo + versione SNMP al momento della lettura.</summary>
    private readonly ConcurrentDictionary<Guid, (string Signature, DateTimeOffset Next)> schedule = new();
    private readonly ConcurrentDictionary<Guid, byte> requested = new();
    private readonly SemaphoreSlim wakeUp = new(0, 1);

    /// <summary>Inventario immediato del device (richiesta dalla UI tramite l'hub agent).</summary>
    public void Request(Guid deviceId)
    {
        requested[deviceId] = 0;
        try { wakeUp.Release(); } catch (SemaphoreFullException) { }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = MaxParallel, CancellationToken = stoppingToken };
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (targets.Current is { } list)
                {
                    var due = DueTargets(list).ToList();
                    await Parallel.ForEachAsync(due, parallel, async (target, ct) => await InventoryAsync(target, ct));
                }

                await wakeUp.WaitAsync(CheckInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private IEnumerable<AgentTargetDto> DueTargets(IReadOnlyList<AgentTargetDto> list)
    {
        var now = time.GetUtcNow();
        var ids = list.Select(t => t.DeviceId).ToHashSet();
        foreach (var id in schedule.Keys.Where(id => !ids.Contains(id)))
            schedule.TryRemove(id, out _);

        foreach (var target in list)
        {
            var wanted = requested.TryRemove(target.DeviceId, out _);
            if (target.SnmpVersion != SnmpVersion.V2c)
            {
                if (wanted)
                    logger.LogInformation("{Name}: elenco interfacce non disponibile, serve SNMP v2c", target.Name);
                continue;
            }

            if (wanted || !schedule.TryGetValue(target.DeviceId, out var entry) || entry.Signature != Signature(target) || now >= entry.Next)
                yield return target;
        }
    }

    private async Task InventoryAsync(AgentTargetDto target, CancellationToken ct)
    {
        var o = options.Value;
        var ok = false;
        try
        {
            var address = await icmp.ResolveAsync(target.Address, TimeSpan.FromMilliseconds(o.IcmpTimeoutMs), ct);
            if (address is not null && await ReadInterfacesAsync(address, target.SnmpCommunity ?? o.SnmpCommunity, o, ct) is { Count: > 0 } interfaces)
            {
                ok = await api.SendInterfacesAsync(new AgentInterfacesReportDto(agentOptions.Value.AgentId, target.DeviceId, interfaces), ct);
                if (ok)
                    logger.LogInformation("{Name}: inventario di {Count} interfacce inviato", target.Name, interfaces.Count);
            }
            else
            {
                logger.LogDebug("{Name} ({Address}): inventario interfacce non riuscito, nuovo tentativo tra {Minutes} min",
                    target.Name, target.Address, RetryAfterError.TotalMinutes);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Errore nell'inventario delle interfacce di {Name}", target.Name);
        }

        var next = time.GetUtcNow() + (ok ? TimeSpan.FromMinutes(o.InterfaceInventoryMinutes) : RetryAfterError);
        schedule[target.DeviceId] = (Signature(target), next);
    }

    /// <summary>
    /// Le righe sono quelle di ifName (ifXTable) più quelle di ifOperStatus (ifTable), unite per ifIndex. Senza ifXTable
    /// il nome viene da ifDescr ("WAN1") e la velocità da ifSpeed (bps, al massimo 4,29 Gbit/s).
    /// </summary>
    private async Task<List<DeviceInterfaceDto>?> ReadInterfacesAsync(IPAddress address, string community, PollingOptions o, CancellationToken ct)
    {
        var timeout = TimeSpan.FromMilliseconds(o.SnmpTimeoutMs);
        var names = await snmp.WalkColumnAsync(address, community, IfName, timeout, ct);
        if (names is null)
            return null; // non risponde: inutile chiedere le altre colonne

        var aliases = await snmp.WalkColumnAsync(address, community, IfAlias, timeout, ct) ?? [];
        var speeds = await snmp.WalkColumnAsync(address, community, IfHighSpeed, timeout, ct) ?? [];
        var oper = await snmp.WalkColumnAsync(address, community, IfOperStatus, timeout, ct) ?? [];
        var rows = names.Keys.Union(oper.Keys).Order().ToList();

        var descriptions = rows.Any(i => Text(names.GetValueOrDefault(i), 128) is null)
            ? await snmp.WalkColumnAsync(address, community, IfDescr, timeout, ct) ?? []
            : [];
        var legacySpeeds = speeds.Count == 0 ? await snmp.WalkColumnAsync(address, community, IfSpeed, timeout, ct) ?? [] : [];

        return rows.Select(i => new DeviceInterfaceDto(
            i,
            Text(names.GetValueOrDefault(i), 128) ?? Text(descriptions.GetValueOrDefault(i), 128),
            Text(aliases.GetValueOrDefault(i), 256),
            speeds.GetValueOrDefault(i) is Gauge32 g && g.ToUInt32() > 0 ? g.ToUInt32() * 1_000_000L
                : legacySpeeds.GetValueOrDefault(i) is Gauge32 b && b.ToUInt32() is > 0 and < uint.MaxValue ? b.ToUInt32() : null,
            oper.GetValueOrDefault(i) is Integer32 s ? s.ToInt32() switch { 1 => InterfaceOperStatus.Up, 2 => InterfaceOperStatus.Down, _ => InterfaceOperStatus.Other }
                                                    : InterfaceOperStatus.Other)).ToList();
    }

    private static string? Text(ISnmpData? data, int maxLength) =>
        data is OctetString s && s.ToString() is { Length: > 0 } text ? text[..Math.Min(text.Length, maxLength)] : null;

    // Community come hash: cambiarla rifà l'inventario, senza tenerla in chiaro in un'altra struttura
    private static string Signature(AgentTargetDto t) => $"{t.Address}|{t.SnmpVersion}|{string.GetHashCode(t.SnmpCommunity)}";

    public override void Dispose()
    {
        wakeUp.Dispose();
        base.Dispose();
    }
}
