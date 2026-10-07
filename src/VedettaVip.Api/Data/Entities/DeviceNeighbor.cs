// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

namespace VedettaVip.Api.Data.Entities;

/// <summary>
/// Vicino visto da un device nell'ultima lettura dell'agente (/ip/neighbor, LLDP, CDP): base delle proposte della
/// Discovery. Sostituito per intero a ogni lettura; si elimina con il device.
/// </summary>
public sealed class DeviceNeighbor
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }
    public required string Protocol { get; set; }
    public string? LocalInterface { get; set; }
    public int? LocalIfIndex { get; set; }
    public string? Identity { get; set; }
    public string? Address { get; set; }
    public string? MacAddress { get; set; }
    public string? RemoteInterface { get; set; }
    public string? Platform { get; set; }
    public string? Version { get; set; }
    public string? Board { get; set; }
    public string? Capabilities { get; set; }
    /// <summary>Orologio dell'API alla ricezione.</summary>
    public DateTimeOffset SeenAt { get; set; }
}

/// <summary>Proposta della Discovery scartata ("Ignora"): non viene più mostrata.</summary>
public sealed class DiscoveryIgnore
{
    public required string Key { get; set; }
    public string? Label { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Voce della tabella ARP di un router nell'ultima lettura, con hostname/commento del lease DHCP se c'è.</summary>
public sealed class DeviceArpEntry
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }
    public required string Address { get; set; }
    public string? MacAddress { get; set; }
    public string? Interface { get; set; }
    public int? IfIndex { get; set; }
    public string? HostName { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset SeenAt { get; set; }
}

/// <summary>Scansione di una subnet chiesta dalla pagina Scoperta ed eseguita da un agente.</summary>
public sealed class DiscoveryScan
{
    public Guid Id { get; set; }
    public required string Cidr { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? MapId { get; set; }
    public VedettaVip.Shared.Contracts.DiscoveryScanStatus Status { get; set; }
    /// <summary>Agente che ha preso la scansione (le altre richieste dello stesso compito ricevono 409).</summary>
    public string? AgentId { get; set; }
    public int Scanned { get; set; }
    public int Total { get; set; }
    public string? Error { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public List<DiscoveryScanHost> Hosts { get; set; } = [];
}

public sealed class DiscoveryScanHost
{
    public Guid Id { get; set; }
    public Guid ScanId { get; set; }
    public required string Address { get; set; }
    public double? RttMs { get; set; }
    public string? DnsName { get; set; }
    public string? SysName { get; set; }
    public string? SysDescr { get; set; }
    public Guid? SnmpProfileId { get; set; }
    public bool SnmpFallback { get; set; }
    /// <summary>Porte TCP aperte, separate da virgola ("22,80,443").</summary>
    public string? OpenPorts { get; set; }
}
