// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Net;
using Lextm.SharpSnmpLib;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Worker.Probes;

/// <summary>
/// Conversione dei vicini nelle tre fonti supportate (funzioni pure, coperte da test):
/// <list type="bullet">
/// <item>RouterOS /ip/neighbor: MNDP, CDP e LLDP già uniti dal router;</item>
/// <item>LLDP-MIB (IEEE 802.1AB) via SNMP: switch gestiti di quasi tutti i produttori;</item>
/// <item>CISCO-CDP-MIB via SNMP: apparati Cisco.</item>
/// </list>
/// </summary>
public static class NeighborParsers
{
    // ---------- RouterOS ----------

    /// <summary>
    /// Righe di /ip/neighbor/print. "interface" in v7 può essere "ether2,bridge" (porta e bridge): conta la porta.
    /// "address4" esiste da v7; in v6 "address" è l'IPv4.
    /// </summary>
    public static List<NeighborDto> FromRouterOs(IReadOnlyList<IReadOnlyDictionary<string, string>> rows) =>
        rows.Select(r => new NeighborDto(
                Protocol: Cut(r.GetValueOrDefault("discovered-by"), 32) ?? "mndp",
                LocalInterface: Cut(Blank(r.GetValueOrDefault("interface"))?.Split(',')[0], 128),
                LocalIfIndex: null,
                Identity: Cut(Blank(r.GetValueOrDefault("identity")), 128),
                Address: Ipv4(Blank(r.GetValueOrDefault("address4"))) ?? Ipv4(Blank(r.GetValueOrDefault("address"))),
                MacAddress: Cut(Blank(r.GetValueOrDefault("mac-address"))?.ToUpperInvariant(), 32),
                RemoteInterface: Cut(Blank(r.GetValueOrDefault("interface-name")), 128),
                Platform: Cut(Blank(r.GetValueOrDefault("platform")), 128),
                Version: Cut(Blank(r.GetValueOrDefault("version")), 64),
                Board: Cut(Blank(r.GetValueOrDefault("board")), 64),
                Capabilities: Cut(Blank(r.GetValueOrDefault("system-caps-enabled")) ?? Blank(r.GetValueOrDefault("system-caps")), 128)))
            .Where(Identifiable)
            .ToList();

    // ---------- LLDP-MIB ----------

    public const string LldpRemTable = "1.0.8802.1.1.2.1.4.1.1";
    public const string LldpRemManAddrTable = "1.0.8802.1.1.2.1.4.2.1";
    public const string LldpLocPortTable = "1.0.8802.1.1.2.1.3.7.1";

    /// <summary>
    /// <paramref name="remote"/>: walk di lldpRemTable (indice colonna.timeMark.porta.remIndex);
    /// <paramref name="managementAddresses"/>: walk di lldpRemManAddrTable (l'indirizzo è nell'indice);
    /// <paramref name="localPorts"/>: walk di lldpLocPortTable (colonna.porta). La porta locale è spesso l'ifIndex.
    /// </summary>
    public static List<NeighborDto> FromLldp(
        IReadOnlyList<(uint[] Index, ISnmpData Data)> remote,
        IReadOnlyList<(uint[] Index, ISnmpData Data)> managementAddresses,
        IReadOnlyList<(uint[] Index, ISnmpData Data)> localPorts)
    {
        // Riga = (porta locale, remIndex); il timeMark cambia a ogni aggiornamento e non identifica il vicino
        var rows = remote.Where(x => x.Index.Length >= 4)
            .GroupBy(x => (Port: x.Index[2], Rem: x.Index[3]))
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.Index[0], x => x.Data));
        var ports = localPorts.Where(x => x.Index.Length >= 2)
            .GroupBy(x => x.Index[1])
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.Index[0], x => x.Data));

        // lldpRemManAddrTable: colonna.timeMark.porta.remIndex.subtype.lunghezza.byte... (subtype 1 = IPv4)
        var addresses = new Dictionary<(uint, uint), string>();
        foreach (var (index, _) in managementAddresses)
            if (index.Length >= 10 && index[4] == 1 && index[5] == 4)
                addresses.TryAdd((index[2], index[3]), string.Join('.', index[6..10]));

        var result = new List<NeighborDto>();
        foreach (var ((port, rem), c) in rows)
        {
            var portId = Text(c.GetValueOrDefault(7u));
            var portDesc = Text(c.GetValueOrDefault(8u));
            var chassisIsMac = c.GetValueOrDefault(4u) is Integer32 { } cs && cs.ToInt32() == 4;
            var portIdIsMac = c.GetValueOrDefault(6u) is Integer32 { } ps && ps.ToInt32() == 3;
            var local = ports.GetValueOrDefault(port);
            result.Add(new NeighborDto(
                Protocol: "lldp",
                LocalInterface: Cut(Text(local?.GetValueOrDefault(4u)) ?? Text(local?.GetValueOrDefault(3u)), 128),
                LocalIfIndex: (int)port,
                Identity: Cut(Text(c.GetValueOrDefault(9u)), 128),
                Address: addresses.GetValueOrDefault((port, rem)),
                MacAddress: chassisIsMac ? Mac(c.GetValueOrDefault(5u)) : null,
                // Un port ID di tipo MAC non dice niente a chi guarda la mappa: meglio la descrizione
                RemoteInterface: Cut(portIdIsMac ? portDesc ?? Mac(c.GetValueOrDefault(7u)) : portId ?? portDesc, 128),
                Platform: Cut(FirstLine(Text(c.GetValueOrDefault(10u))), 128),
                Version: null,
                Board: null,
                Capabilities: LldpCapabilities(c.GetValueOrDefault(12u))));
        }
        return result.Where(Identifiable).ToList();
    }

    /// <summary>Un vicino senza nome, IP né MAC non si può proporre né riconoscere (righe LLDP incomplete).</summary>
    private static bool Identifiable(NeighborDto n) => n.Identity is not null || n.Address is not null || n.MacAddress is not null;

    /// <summary>lldpRemSysCapEnabled (BITS, bit 0 = MSB del primo byte): bridge, router, wlan-ap, telephone...</summary>
    public static string? LldpCapabilities(ISnmpData? data)
    {
        if (data is not OctetString s || s.GetRaw() is not { Length: > 0 } raw)
            return null;
        string[] names = ["other", "repeater", "bridge", "wlan-ap", "router", "telephone", "docsis", "station"];
        var caps = names.Where((_, bit) => bit / 8 < raw.Length && (raw[bit / 8] & (0x80 >> (bit % 8))) != 0).ToList();
        return caps.Count > 0 ? string.Join(',', caps) : null;
    }

    // ---------- CISCO-CDP-MIB ----------

    public const string CdpCacheTable = "1.3.6.1.4.1.9.9.23.1.2.1.1";

    /// <summary>Walk di cdpCacheTable: indice colonna.ifIndex.devIndex (l'ifIndex locale è nell'indice).</summary>
    public static List<NeighborDto> FromCdp(IReadOnlyList<(uint[] Index, ISnmpData Data)> cache)
    {
        var rows = cache.Where(x => x.Index.Length >= 3)
            .GroupBy(x => (IfIndex: x.Index[1], Dev: x.Index[2]))
            .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.Index[0], x => x.Data));

        return rows.Select(r =>
        {
            var c = r.Value;
            var address = c.GetValueOrDefault(3u) is Integer32 type && type.ToInt32() == 1 && c.GetValueOrDefault(4u) is OctetString a
                          && a.GetRaw() is { Length: 4 } bytes ? new IPAddress(bytes).ToString() : null;
            return new NeighborDto(
                Protocol: "cdp",
                LocalInterface: null,
                LocalIfIndex: (int)r.Key.IfIndex,
                Identity: Cut(Text(c.GetValueOrDefault(6u)), 128),
                Address: address,
                MacAddress: null,
                RemoteInterface: Cut(Text(c.GetValueOrDefault(7u)), 128),
                Platform: Cut(Text(c.GetValueOrDefault(8u)), 128),
                Version: null,
                Board: null,
                Capabilities: CdpCapabilities(c.GetValueOrDefault(9u)));
        }).Where(Identifiable).ToList();
    }

    /// <summary>cdpCacheCapabilities: 4 byte big-endian (0x01 router, 0x08 switch, 0x80 telefono...).</summary>
    public static string? CdpCapabilities(ISnmpData? data)
    {
        if (data is not OctetString s || s.GetRaw() is not { Length: 4 } raw)
            return null;
        var bits = (uint)(raw[0] << 24 | raw[1] << 16 | raw[2] << 8 | raw[3]);
        (uint Bit, string Name)[] names = [(0x01, "router"), (0x02, "bridge"), (0x08, "switch"), (0x10, "host"), (0x80, "telephone")];
        var caps = names.Where(n => (bits & n.Bit) != 0).Select(n => n.Name).ToList();
        return caps.Count > 0 ? string.Join(',', caps) : null;
    }

    // ---------- ARP ----------

    public const string IpNetToMediaPhysAddress = "1.3.6.1.2.1.4.22.1.2";
    public const string IpNetToMediaType = "1.3.6.1.2.1.4.22.1.4";

    /// <summary>
    /// /ip/arp/print unito ai lease DHCP del router (per MAC, poi per indirizzo): hostname e commento danno un nome
    /// agli host. Scartate le voci senza MAC (incomplete), disabilitate o "failed".
    /// </summary>
    public static List<ArpEntryDto> FromRouterOsArp(
        IReadOnlyList<IReadOnlyDictionary<string, string>> arp, IReadOnlyList<IReadOnlyDictionary<string, string>> leases)
    {
        var byMac = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var byAddress = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        foreach (var l in leases)
        {
            if (Blank(l.GetValueOrDefault("mac-address")) is { } mac) byMac.TryAdd(mac, l);
            if ((Ipv4(Blank(l.GetValueOrDefault("active-address"))) ?? Ipv4(Blank(l.GetValueOrDefault("address")))) is { } ip) byAddress.TryAdd(ip, l);
        }

        return arp
            .Where(r => r.GetValueOrDefault("disabled") != "true" && r.GetValueOrDefault("invalid") != "true"
                        && r.GetValueOrDefault("status") is not ("failed" or "incomplete"))
            .Select(r => (Address: Ipv4(Blank(r.GetValueOrDefault("address"))), Mac: Blank(r.GetValueOrDefault("mac-address"))?.ToUpperInvariant(), Row: r))
            .Where(x => x.Address is not null && x.Mac is not null)
            .Select(x =>
            {
                var lease = byMac.GetValueOrDefault(x.Mac!) ?? byAddress.GetValueOrDefault(x.Address!);
                return new ArpEntryDto(x.Address!, x.Mac, Cut(Blank(x.Row.GetValueOrDefault("interface")), 128), null,
                    Cut(Blank(lease?.GetValueOrDefault("host-name")), 128), Cut(Blank(lease?.GetValueOrDefault("comment")), 256));
            })
            .DistinctBy(e => e.Address)
            .ToList();
    }

    /// <summary>ipNetToMediaPhysAddress (indice ifIndex.a.b.c.d); escluse le voci di tipo invalid (2).</summary>
    public static List<ArpEntryDto> FromSnmpArp(IReadOnlyList<(uint[] Index, ISnmpData Data)> physAddress, IReadOnlyList<(uint[] Index, ISnmpData Data)> types)
    {
        var invalid = types.Where(t => t.Index.Length == 5 && t.Data is Integer32 i && i.ToInt32() == 2)
            .Select(t => string.Join('.', t.Index)).ToHashSet();
        return physAddress
            .Where(x => x.Index.Length == 5 && !invalid.Contains(string.Join('.', x.Index)) && Mac(x.Data) is not null)
            .Select(x => new ArpEntryDto(string.Join('.', x.Index[1..5]), Mac(x.Data), null, (int)x.Index[0], null, null))
            .DistinctBy(e => e.Address)
            .ToList();
    }

    // ---------- Supporto ----------

    private static string? Ipv4(string? s) =>
        s is not null && IPAddress.TryParse(s, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? s : null;

    /// <summary>Testo stampabile; null se vuoto o binario (es. un MAC in una OctetString).</summary>
    private static string? Text(ISnmpData? data)
    {
        if (data is not OctetString s)
            return null;
        var raw = s.GetRaw();
        if (raw.Length == 0 || raw.Any(b => b < 0x20 && b is not (9 or 10 or 13)))
            return null;
        var text = System.Text.Encoding.UTF8.GetString(raw).Trim('\0', ' ');
        return text.Length > 0 ? text : null;
    }

    private static string? Mac(ISnmpData? data) =>
        data is OctetString s && s.GetRaw() is { Length: 6 } raw ? string.Join(':', raw.Select(b => b.ToString("X2"))) : null;

    private static string? FirstLine(string? s) => s?.Split('\n', '\r').FirstOrDefault(l => l.Trim().Length > 0)?.Trim();

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? Cut(string? s, int max) => s is null ? null : s[..Math.Min(s.Length, max)];
}
