// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.ComponentModel.DataAnnotations;

namespace VedettaVip.Shared.Contracts;

/// <summary>
/// Vicino visto da un dispositivo: da /ip/neighbor (RouterOS: MNDP, CDP, LLDP) o via SNMP (LLDP-MIB, CISCO-CDP-MIB).
/// </summary>
/// <param name="Protocol">"mndp", "lldp", "cdp" (RouterOS può riportarne più d'uno: "mndp,lldp").</param>
/// <param name="LocalInterface">Interfaccia del dispositivo che vede il vicino (es. "ether2", "Gi1/0/1").</param>
/// <param name="LocalIfIndex">IfIndex locale quando la fonte lo dà (CDP, LLDP con numerazione = ifIndex).</param>
/// <param name="RemoteInterface">Interfaccia del vicino verso di noi, se annunciata.</param>
public sealed record NeighborDto(
    [property: StringLength(32)] string Protocol,
    [property: StringLength(128)] string? LocalInterface,
    int? LocalIfIndex,
    [property: StringLength(128)] string? Identity,
    [property: StringLength(64)] string? Address,
    [property: StringLength(32)] string? MacAddress,
    [property: StringLength(128)] string? RemoteInterface,
    [property: StringLength(128)] string? Platform,
    [property: StringLength(64)] string? Version,
    [property: StringLength(64)] string? Board,
    [property: StringLength(128)] string? Capabilities);

/// <summary>
/// Host nella tabella ARP di un router (RouterOS /ip/arp, SNMP ipNetToMediaTable), arricchito con il lease DHCP
/// del router se c'è (hostname dichiarato dal client, commento messo dall'amministratore).
/// </summary>
public sealed record ArpEntryDto(
    [property: StringLength(64)] string Address,
    [property: StringLength(32)] string? MacAddress,
    [property: StringLength(128)] string? Interface,
    int? IfIndex,
    [property: StringLength(128)] string? HostName,
    [property: StringLength(256)] string? Comment);

/// <summary>
/// Vicini e tabella ARP di un dispositivo (POST /api/agent/neighbors): sostituiscono quelli della lettura precedente.
/// Arp null = non letta (il dispositivo non è un router o la lettura è fallita): resta quella precedente.
/// </summary>
public sealed record AgentNeighborsReportDto(
    [property: Required, StringLength(64, MinimumLength = 1)] string AgentId,
    Guid DeviceId,
    [property: Required, MaxLength(2000)] IReadOnlyList<NeighborDto> Neighbors,
    [property: MaxLength(20_000)] IReadOnlyList<ArpEntryDto>? Arp = null);

/// <summary>Un dispositivo che ha visto il vicino proposto, e su quale interfaccia.</summary>
public sealed record DiscoverySightingDto(Guid DeviceId, string DeviceName, string? LocalInterface, string? RemoteInterface, string Protocol);

/// <summary>Dispositivo non ancora in VedettaVip visto come vicino; i suggerimenti vengono dal (primo) dispositivo che l'ha visto.</summary>
/// <param name="Key">Identifica la proposta (anche per "Ignora"): MAC, altrimenti indirizzo, altrimenti nome.</param>
/// <param name="Problem">Motivo per cui non si può aggiungere così com'è (es. nessun IPv4 annunciato); null = pronto.</param>
public sealed record DiscoveryDeviceProposalDto(
    string Key,
    string Name,
    string? Address,
    string? MacAddress,
    DeviceType Type,
    bool RouterOs,
    string? Platform,
    string? Version,
    string? Board,
    IReadOnlyList<DiscoverySightingDto> SeenBy,
    Guid? SuggestedCustomerId,
    Guid? SuggestedMapId,
    Guid? SuggestedParentId,
    string? Problem,
    /// <summary>Origini che l'hanno trovato: "vicini", "arp", "scansione" (separate da virgola).</summary>
    string Sources = "",
    /// <summary>Descrizione: sysDescr, hostname DHCP o nome DNS (per riconoscerlo).</summary>
    string? Description = null,
    IReadOnlyList<int>? OpenPorts = null,
    /// <summary>La scansione ha avuto risposta SNMP v2c: il dispositivo nasce con SNMP e questo profilo (null = community del Worker).</summary>
    bool Snmp = false,
    Guid? SnmpProfileId = null,
    /// <summary>Produttore dal MAC (registri IEEE), es. "Yealink", o "MAC privato (casuale)".</summary>
    string? Vendor = null,
    /// <summary>Scansione da cui viene (per il filtro "visto da" e per "Elimina"); null = solo vicini/ARP.</summary>
    Guid? ScanId = null);

/// <summary>Link mancante fra due dispositivi già in VedettaVip che si vedono come vicini.</summary>
/// <param name="MapId">Mappa in comune su cui disegnarlo; null = nessuna mappa in comune (Problem spiega).</param>
/// <param name="SourceDeviceId">Dispositivo e IfIndex da cui leggere il traffico (interfaccia nota dall'inventario SNMP).</param>
public sealed record DiscoveryLinkProposalDto(
    string Key,
    Guid FromDeviceId,
    string FromName,
    string? FromInterface,
    Guid ToDeviceId,
    string ToName,
    string? ToInterface,
    Guid? MapId,
    string? MapName,
    Guid? SourceDeviceId,
    int? SourceIfIndex,
    long SpeedBps,
    string Protocols,
    string? Problem);

/// <param name="LastSeen">Lettura dei vicini più recente fra tutti i dispositivi (null = mai).</param>
public sealed record DiscoveryResultDto(
    DateTimeOffset? LastSeen,
    int DevicesScanned,
    IReadOnlyList<DiscoveryDeviceProposalDto> Devices,
    IReadOnlyList<DiscoveryLinkProposalDto> Links,
    int Ignored);

/// <summary>Dispositivo da aggiungere: i valori della proposta, eventualmente corretti nella pagina.</summary>
public sealed record DiscoveryDeviceAcceptDto(
    [property: Required] string Key,
    [property: Required(ErrorMessage = "Il nome è obbligatorio."), StringLength(128)] string Name,
    [property: Required(ErrorMessage = "L'indirizzo è obbligatorio."), StringLength(255)] string Address,
    DeviceType Type,
    bool RouterOs,
    Guid? CustomerId,
    Guid? MapId,
    Guid? ParentId,
    bool Snmp = false,
    Guid? SnmpCredentialId = null);

public sealed record DiscoveryAcceptDto(
    [property: MaxLength(500)] IReadOnlyList<DiscoveryDeviceAcceptDto> Devices,
    [property: MaxLength(1000)] IReadOnlyList<string> LinkKeys);

/// <summary>Esito: dispositivi e link creati (i link verso i dispositivi nuovi sono compresi).</summary>
public sealed record DiscoveryAcceptResultDto(int DevicesCreated, int LinksCreated, IReadOnlyList<string> Warnings);

public sealed record DiscoveryIgnoreDto([property: MaxLength(1000)] IReadOnlyList<string> Keys, bool Ignore = true);

/// <summary>Proposte di dispositivi da eliminare: si cancellano le letture salvate (ARP, vicini, host delle scansioni).</summary>
public sealed record DiscoveryForgetDto([property: Required, MaxLength(1000)] IReadOnlyList<string> Keys);

/// <summary>Letture cancellate da "Elimina".</summary>
public sealed record DiscoveryForgetResultDto(int ArpEntries, int Neighbors, int ScanHosts);

// ---------- Scansione di subnet ----------

public enum DiscoveryScanStatus { Pending, Running, Done, Failed }

/// <summary>Community da provare in una scansione: quella di un profilo SNMP (ProfileId) o del Worker (null).</summary>
public sealed record ScanCommunityDto(Guid? ProfileId, string Community)
{
    /// <summary>Il record stampato nei log non deve contenere la community.</summary>
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append($"ProfileId = {ProfileId}");
        return true;
    }
}

/// <summary>Compito di scansione per l'agente (GET /api/agent/scans/{id}).</summary>
public sealed record AgentScanRequestDto(Guid ScanId, string Cidr, IReadOnlyList<ScanCommunityDto> Communities);

/// <summary>Host vivo trovato dalla scansione.</summary>
/// <param name="SnmpProfileId">Profilo SNMP la cui community ha risposto; null con <paramref name="SnmpFallback"/> = community del Worker.</param>
/// <param name="OpenPorts">Porte TCP aperte fra quelle provate (22, 80, 443, 554, 8728, 8729, 9100...).</param>
public sealed record ScanHostDto(
    [property: StringLength(64)] string Address,
    double? RttMs,
    [property: StringLength(255)] string? DnsName,
    [property: StringLength(128)] string? SysName,
    [property: StringLength(512)] string? SysDescr,
    Guid? SnmpProfileId,
    bool SnmpFallback,
    [property: MaxLength(32)] IReadOnlyList<int> OpenPorts);

/// <summary>Avanzamento o esito di una scansione (POST /api/agent/scans/{id}).</summary>
public sealed record AgentScanResultDto(
    [property: Required, StringLength(64, MinimumLength = 1)] string AgentId,
    DiscoveryScanStatus Status,
    int Scanned,
    int Total,
    [property: MaxLength(4096)] IReadOnlyList<ScanHostDto> Hosts,
    [property: StringLength(512)] string? Error = null);

public sealed record DiscoveryScanDto(
    Guid Id,
    string Cidr,
    Guid? CustomerId,
    Guid? MapId,
    DiscoveryScanStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    int Scanned,
    int Total,
    int HostsAlive,
    string? Error,
    string? CreatedBy);

/// <param name="Cidr">Rete IPv4, es. "192.0.2.0/24" (da /22 a /32).</param>
public sealed record DiscoveryScanCreateDto(
    [property: Required(ErrorMessage = "Indicare la rete da scansionare."), StringLength(32)] string Cidr,
    Guid? CustomerId = null,
    Guid? MapId = null);
