// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.ComponentModel.DataAnnotations;
using VedettaVip.Shared.Models;

namespace VedettaVip.Shared.Contracts;

/// <summary>
/// Dispositivo da interrogare, restituito da GET /api/agent/targets. IfIndexes: interfacce da misurare,
/// cioè quelle usate come sorgente di traffico da almeno un link (vuoto = nessuna). Thresholds: soglie
/// effettive del device (specifiche o generali); null solo da un'API che non le fornisce (si usano quelle locali).
/// </summary>
/// <param name="IsRouter">Router o firewall: da questi la Discovery legge la tabella ARP via SNMP.</param>
/// <param name="RouterOs">Accesso all'API RouterOS (solo device con API abilitata e un profilo effettivo); mai nei log.</param>
/// <param name="SnmpCommunity">
/// Community del profilo SNMP effettivo (device → cliente → predefinito), in chiaro: viaggia solo verso gli agenti
/// autenticati. Null = l'agente usa la propria configurazione (Polling:SnmpCommunity). Mai nei log.
/// </param>
public sealed record AgentTargetDto(
    Guid DeviceId, string Name, string Address, SnmpVersion SnmpVersion,
    IReadOnlyList<int>? IfIndexes = null, DetectionThresholdsDto? Thresholds = null, string? SnmpCommunity = null,
    RouterOsTargetDto? RouterOs = null,
    bool IsRouter = false)
{
    /// <summary>Il record stampato nei log non deve contenere la community.</summary>
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append($"DeviceId = {DeviceId}, Name = {Name}, Address = {Address}, SnmpVersion = {SnmpVersion}");
        return true;
    }

    public IReadOnlyList<int> IfIndexes { get; init; } = IfIndexes ?? [];
}

/// <summary>
/// Report inviato dal Worker a POST /api/agent/status: cambi di stato (subito) oppure snapshot
/// completo (periodico). Gli stati Unknown non vengono mai inviati.
/// </summary>
public sealed record AgentStatusReportDto(
    [property: Required, StringLength(64, MinimumLength = 1)] string AgentId,
    bool IsSnapshot,
    [property: Required, MaxLength(10_000)] IReadOnlyList<DeviceStatusResultDto> Results);

/// <param name="Time">Istante (UTC) in cui il Worker ha rilevato lo stato.</param>
public sealed record DeviceStatusResultDto(
    Guid DeviceId,
    NodeState State,
    DateTimeOffset Time,
    double? RttMs,
    bool? SnmpOk);

/// <summary>
/// Campioni inviati dal Worker a POST /api/agent/traffic dopo ogni ciclo di polling: traffico delle interfacce
/// (anche dato live per la mappa) ed esito dei ping (solo storico).
/// </summary>
public sealed record AgentTrafficReportDto(
    [property: Required, StringLength(64, MinimumLength = 1)] string AgentId,
    [property: Required, MaxLength(100_000)] IReadOnlyList<InterfaceTrafficDto> Samples,
    [property: MaxLength(100_000)] IReadOnlyList<DevicePingDto>? Pings = null);

/// <summary>Esito di un ping: RttMs valorizzato solo se riuscito (un ping perso non è un RTT).</summary>
/// <param name="Time">Istante (UTC, orologio dell'agente) del ping.</param>
public sealed record DevicePingDto(Guid DeviceId, DateTimeOffset Time, bool Success, double? RttMs);

/// <summary>
/// Traffico medio di un'interfaccia tra due poll consecutivi (contatori ifHC*, tempo da sysUpTime del device).
/// In/Out sono dal punto di vista dell'interfaccia. Inviato anche ai browser ("TrafficUpdated").
/// </summary>
/// <param name="Time">Istante (UTC, orologio dell'agente) del campione più recente.</param>
/// <param name="SpeedBps">Velocità rilevata (ifHighSpeed), null se non disponibile o 0.</param>
/// <param name="Name">ifName (es. "ether1", "sfp-sfpplus1").</param>
public sealed record InterfaceTrafficDto(
    Guid DeviceId,
    int IfIndex,
    DateTimeOffset Time,
    long InBps,
    long OutBps,
    long? SpeedBps,
    string? Name);

/// <summary>Stato operativo di un'interfaccia (ifOperStatus); i valori diversi da up/down sono Other.</summary>
public enum InterfaceOperStatus { Up, Down, Other }

/// <summary>Interfaccia di un device letta da ifTable/ifXTable (inventario SNMP dell'agente).</summary>
/// <param name="SpeedBps">ifHighSpeed in bps; null se 0 o non disponibile.</param>
/// <param name="Alias">ifAlias: il "comment" dell'interfaccia su RouterOS.</param>
public sealed record DeviceInterfaceDto(
    int IfIndex,
    [property: StringLength(128)] string? Name,
    [property: StringLength(256)] string? Alias,
    long? SpeedBps,
    InterfaceOperStatus OperStatus);

/// <summary>Inventario completo delle interfacce di un device: sostituisce quello salvato (POST /api/agent/interfaces).</summary>
public sealed record AgentInterfacesReportDto(
    [property: Required, StringLength(64, MinimumLength = 1)] string AgentId,
    Guid DeviceId,
    [property: Required, MaxLength(10_000)] IReadOnlyList<DeviceInterfaceDto> Interfaces);

/// <summary>Interfacce di un device per la UI, con l'istante dell'ultimo inventario (null = mai letto).</summary>
public sealed record DeviceInterfacesDto(Guid DeviceId, DateTimeOffset? UpdatedAt, IReadOnlyList<DeviceInterfaceDto> Interfaces);

public sealed record AgentStatusAckDto(int Accepted, int Changed, IReadOnlyList<Guid> UnknownDeviceIds);

/// <summary>Messaggio SignalR "DeviceStateChanged" sull'hub /hubs/status.</summary>
public sealed record DeviceStateChangedDto(Guid DeviceId, NodeState State, DateTimeOffset Since);

/// <summary>Agente noto all'API, restituito da GET /api/agents.</summary>
public sealed record AgentDto(string AgentId, DateTimeOffset LastSeen, bool IsOnline);

public static class AgentHeaders
{
    /// <summary>Header con la chiave condivisa tra Worker (agente) e API.</summary>
    public const string ApiKey = "X-Agent-Key";
}

/// <summary>Credenziali e trasporto per l'API RouterOS di un target (password in chiaro: solo verso agenti autenticati).</summary>
public sealed record RouterOsTargetDto(string Username, string Password, bool UseTls, int Port, bool VerifyCertificate)
{
    /// <summary>Il record stampato nei log non deve contenere la password.</summary>
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append($"Username = {Username}, UseTls = {UseTls}, Port = {Port}");
        return true;
    }
}

/// <summary>
/// Lettura di risorse e salute di un router via API (POST /api/agent/routeros). Ok = false: API non raggiungibile o
/// login fallito, con il motivo in <see cref="Error"/>; i valori assenti sul modello di router restano null.
/// </summary>
public sealed record RouterOsSampleDto(
    Guid DeviceId,
    DateTimeOffset Time,
    bool Ok,
    [property: StringLength(512)] string? Error = null,
    double? CpuLoadPct = null,
    long? MemoryTotalBytes = null,
    long? MemoryFreeBytes = null,
    long? UptimeSeconds = null,
    [property: StringLength(64)] string? Version = null,
    [property: StringLength(64)] string? BoardName = null,
    [property: StringLength(64)] string? Architecture = null,
    double? TemperatureC = null,
    double? VoltageV = null,
    /// <summary>Servizio e porta usati, es. "api-ssl :8729" (per capire da dove arriva la lettura o l'errore).</summary>
    [property: StringLength(32)] string? Service = null,
    /// <summary>Interfacce del router (tutte, compresi i tunnel); null = non lette. Non inoltrate ai browser.</summary>
    [property: MaxLength(5000)] IReadOnlyList<RouterOsInterfaceDto>? Interfaces = null,
    /// <summary>Peer WireGuard (RouterOS v7); null = non letti (v6 o lettura fallita).</summary>
    [property: MaxLength(5000)] IReadOnlyList<WireGuardPeerDto>? WireGuardPeers = null,
    /// <summary>Elementi sorvegliati (interfacce, peer) giù: lo calcola l'API, per la colonna RouterOS.</summary>
    int? WatchProblems = null)
{
    public double? MemoryUsedPct => MemoryTotalBytes is > 0 && MemoryFreeBytes is { } free
        ? Math.Round(100.0 * (MemoryTotalBytes.Value - free) / MemoryTotalBytes.Value, 1)
        : null;
}

public sealed record RouterOsInterfaceDto(
    [property: StringLength(128)] string Name,
    [property: StringLength(64)] string? Type,
    bool Running,
    bool Disabled,
    [property: StringLength(256)] string? Comment = null);

/// <param name="PublicKey">Chiave pubblica del peer: identifica il peer anche se nome o commento cambiano.</param>
/// <param name="LastHandshakeSeconds">Secondi dall'ultimo handshake; null = mai (o non riportato).</param>
public sealed record WireGuardPeerDto(
    [property: StringLength(128)] string PublicKey,
    [property: StringLength(128)] string? Interface,
    [property: StringLength(128)] string? Name,
    [property: StringLength(256)] string? Comment,
    [property: StringLength(128)] string? Endpoint,
    long? LastHandshakeSeconds,
    bool Disabled);

public sealed record AgentRouterOsReportDto(
    [property: Required, StringLength(64, MinimumLength = 1)] string AgentId,
    [property: Required, MaxLength(10_000)] IReadOnlyList<RouterOsSampleDto> Samples);

public static class StatusHubMessages
{
    public const string HubPath = "hubs/status";
    public const string DeviceStateChanged = "DeviceStateChanged";
    /// <summary>Argomento: IReadOnlyList&lt;RouterOsSampleDto&gt; (letture RouterOS dell'ultimo report di un agente).</summary>
    public const string RouterOsUpdated = "RouterOsUpdated";
    /// <summary>Argomento: IReadOnlyList&lt;InterfaceTrafficDto&gt; (i campioni dell'ultimo report di un agente).</summary>
    public const string TrafficUpdated = "TrafficUpdated";
    /// <summary>Argomento: MapsChangedDto (mappe modificate da ricaricare).</summary>
    public const string MapsChanged = "MapsChanged";
}

/// <summary>
/// Hub SignalR riservato agli agenti (header X-Agent-Key). Il server invia "TargetsChanged" (senza argomenti)
/// quando cambia l'elenco dei dispositivi: l'agente ricarica subito i target invece di aspettare il refresh periodico.
/// </summary>
public static class AgentHubMessages
{
    public const string HubPath = "hubs/agent";
    public const string TargetsChanged = "TargetsChanged";
    /// <summary>Argomento: Guid del device di cui rileggere subito l'elenco delle interfacce.</summary>
    public const string InterfacesRequested = "InterfacesRequested";
    /// <summary>Nessun argomento: rileggere subito i vicini di tutti i dispositivi ("Scopri ora").</summary>
    public const string NeighborsRequested = "NeighborsRequested";
    /// <summary>Argomento: Guid della scansione di subnet da eseguire (il compito si legge da GET /api/agent/scans/{id}).</summary>
    public const string ScanRequested = "ScanRequested";
}
