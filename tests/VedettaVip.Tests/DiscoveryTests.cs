// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Lextm.SharpSnmpLib;
using VedettaVip.Api.Services;
using VedettaVip.Shared.Contracts;
using VedettaVip.Worker.Probes;
using static VedettaVip.Api.Services.DiscoveryPlanner;

namespace VedettaVip.Tests;

public class NeighborParserTests
{
    [Fact]
    public void RouterOs_neighbors_v7()
    {
        var rows = NeighborParsers.FromRouterOs(
        [
            new Dictionary<string, string>
            {
                ["interface"] = "ether2,bridge", ["address"] = "fe80::1", ["address4"] = "192.0.2.4", ["mac-address"] = "dc:2c:6e:aa:bb:cc",
                ["identity"] = "CRS326-SW2", ["platform"] = "MikroTik", ["version"] = "7.16.2 (stable)", ["board"] = "CRS326-24G-2S+",
                ["interface-name"] = "ether24", ["discovered-by"] = "mndp,lldp", ["system-caps-enabled"] = "bridge,router"
            },
            new Dictionary<string, string> { ["interface"] = "ether5" } // riga vuota: scartata
        ]);
        var n = Assert.Single(rows);
        Assert.Equal(("ether2", "192.0.2.4", "DC:2C:6E:AA:BB:CC", "ether24", "mndp,lldp"), (n.LocalInterface, n.Address, n.MacAddress, n.RemoteInterface, n.Protocol));
    }

    [Fact]
    public void RouterOs_v6_address_without_address4() =>
        Assert.Equal("10.0.0.2", NeighborParsers.FromRouterOs([new Dictionary<string, string> { ["interface"] = "ether1", ["address"] = "10.0.0.2", ["identity"] = "x" }])[0].Address);

    [Fact]
    public void Lldp_mib_rows_with_management_address_and_local_port()
    {
        // lldpRemTable: colonna.timeMark.porta.remIndex — porta locale 3, vicino 1
        uint[] I(uint column) => [column, 1234, 3, 1];
        var remote = new List<(uint[], ISnmpData)>
        {
            (I(4), new Integer32(4)), (I(5), new OctetString([0x00, 0x11, 0x22, 0x33, 0x44, 0x55])),
            (I(6), new Integer32(5)), (I(7), new OctetString("1/0/24")), (I(8), new OctetString("Uplink")),
            (I(9), new OctetString("sw-piano1")), (I(10), new OctetString("HPE OfficeConnect 1920S\nfirmware...")),
            (I(12), new OctetString([0x20, 0x00])) // bit 2 = bridge
        };
        var addresses = new List<(uint[], ISnmpData)> { ([3, 1234, 3, 1, 1, 4, 10, 0, 0, 9], new Integer32(2)) };
        var ports = new List<(uint[], ISnmpData)> { ([4, 3], new OctetString("ether3")) };

        // Riga incompleta (nessun nome, IP o MAC, come visto su router reali): scartata
        remote.Add(([9, 1234, 5, 2], new OctetString("")));
        var n = Assert.Single(NeighborParsers.FromLldp(remote, addresses, ports));
        Assert.Equal(("lldp", "ether3", 3, "sw-piano1", "10.0.0.9"), (n.Protocol, n.LocalInterface, n.LocalIfIndex, n.Identity, n.Address));
        Assert.Equal(("00:11:22:33:44:55", "1/0/24", "HPE OfficeConnect 1920S", "bridge"), (n.MacAddress, n.RemoteInterface, n.Platform, n.Capabilities));
    }

    [Fact]
    public void Cdp_mib_rows()
    {
        uint[] I(uint column) => [column, 10101, 1];
        var cache = new List<(uint[], ISnmpData)>
        {
            (I(3), new Integer32(1)), (I(4), new OctetString([198, 51, 100, 1])), (I(6), new OctetString("core.example.local")),
            (I(7), new OctetString("GigabitEthernet1/0/48")), (I(8), new OctetString("cisco WS-C2960X-48FPD-L")),
            (I(9), new OctetString([0, 0, 0, 0x28])) // switch + igmp
        };
        var n = Assert.Single(NeighborParsers.FromCdp(cache));
        Assert.Equal(("cdp", 10101, "198.51.100.1", "core.example.local", "GigabitEthernet1/0/48", "switch"),
            (n.Protocol, n.LocalIfIndex!.Value, n.Address, n.Identity, n.RemoteInterface, n.Capabilities));
    }
}

public class DiscoveryPlannerTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid Map1 = Guid.NewGuid(), Map2 = Guid.NewGuid(), Customer = Guid.NewGuid();

    private static readonly DeviceInfo Core = new(A, "core", "10.0.0.1", Customer, HasInterfaces: true);
    private static readonly DeviceInfo Sw = new(B, "sw1", "10.0.0.2", Customer, HasInterfaces: false);

    private static NeighborDto N(string? local, string? identity, string? address, string? remote, string platform = "MikroTik", string? board = null, string? caps = null, string proto = "mndp") =>
        new(proto, local, null, identity, address, null, remote, platform, null, board, caps);

    private static (List<DiscoveryDeviceProposalDto> Devices, List<DiscoveryLinkProposalDto> Links, int Ignored) Plan(
        IReadOnlyList<NeighborInfo> neighbors, IReadOnlyList<LinkInfo>? links = null, IReadOnlySet<string>? ignored = null, bool sameMap = true) =>
        DiscoveryPlanner.Plan([Core, Sw], [new(A, Map1, "Sede"), new(B, sameMap ? Map1 : Map2, sameMap ? "Sede" : "Altra")], links ?? [],
            [new(A, 2, "ether2", 1_000_000_000), new(A, 3, "ether3", 10_000_000_000)], neighbors, ignored ?? new HashSet<string>());

    [Fact]
    public void Both_sides_of_a_cable_become_one_link_with_traffic_source()
    {
        var p = Plan([new(A, N("ether2", "sw1", "10.0.0.2", "ether24")), new(B, N("ether24", "core", "10.0.0.1", "ether2", proto: "lldp"))]);
        var l = Assert.Single(p.Links);
        Assert.Equal(("ether2", "ether24", Map1, A, 2, 1_000_000_000L, "lldp,mndp"),
            (l.FromInterface, l.ToInterface, l.MapId!.Value, l.SourceDeviceId!.Value, l.SourceIfIndex!.Value, l.SpeedBps, l.Protocols));
        Assert.Empty(p.Devices);
    }

    [Fact]
    public void Redundant_uplinks_are_two_links()
    {
        var p = Plan([new(A, N("ether2", "sw1", "10.0.0.2", "ether23")), new(A, N("ether3", "sw1", "10.0.0.2", "ether24"))]);
        Assert.Equal(2, p.Links.Count);
    }

    [Fact]
    public void Already_linked_devices_are_not_proposed() =>
        Assert.Empty(Plan([new(A, N("ether2", "sw1", "10.0.0.2", "ether24"))], links: [new(B, A)]).Links);

    [Fact]
    public void Matching_by_name_when_the_neighbor_announces_another_address() =>
        Assert.Single(Plan([new(A, N("ether2", "SW1", "172.16.0.2", null))]).Links);

    [Fact]
    public void No_common_map_is_reported()
    {
        var l = Assert.Single(Plan([new(A, N("ether2", "sw1", "10.0.0.2", null))], sameMap: false).Links);
        Assert.Null(l.MapId);
        Assert.Contains("nessuna mappa in comune", l.Problem);
    }

    [Fact]
    public void Unknown_neighbor_becomes_a_device_proposal_with_suggestions()
    {
        var p = Plan([new(A, N("ether3", "cAP-uffici", "10.0.0.20", "ether1", board: "cAPGi-5HaxD2HaxD"))]);
        var d = Assert.Single(p.Devices);
        Assert.Equal(("cAP-uffici", "10.0.0.20", DeviceType.AccessPoint, true), (d.Name, d.Address, d.Type, d.RouterOs));
        Assert.Equal((Customer, Map1, A), (d.SuggestedCustomerId!.Value, d.SuggestedMapId!.Value, d.SuggestedParentId!.Value));
        Assert.Equal("ether3", Assert.Single(d.SeenBy).LocalInterface);
        Assert.Null(d.Problem);
    }

    [Fact]
    public void Same_unknown_device_seen_twice_is_one_proposal()
    {
        var p = Plan([new(A, N("ether3", "nas", "10.0.0.30", null, platform: "", caps: "station")), new(B, N("ether5", "nas", "10.0.0.30", null, platform: ""))]);
        var d = Assert.Single(p.Devices);
        Assert.Equal(2, d.SeenBy.Count);
    }

    [Fact]
    public void Ignored_proposals_are_counted_not_shown()
    {
        var n = N("ether3", "phone", "10.0.0.40", null, platform: "Cisco IP Phone");
        var p = Plan([new(A, n)], ignored: new HashSet<string> { DeviceKey(n) });
        Assert.Empty(p.Devices);
        Assert.Equal(1, p.Ignored);
    }

    [Fact]
    public void Real_netgear_seen_by_mikrotik_lldp_mib()
    {
        // Lettura tipica: un router RouterOS (via SNMP) vede un Netgear senza sysName, porta locale "bridge/ether2…"
        var core = Core with { HasInterfaces = true };
        var n = new NeighborDto("lldp", "bridge/ether2", 3, null, "192.0.2.223", null, "1/g24",
            "S3300-52X ProSAFE 48-Port Gigabit Stackable Smart Switch with 4 10G uplinks", null, null, "router");
        var p = DiscoveryPlanner.Plan([core], [new(A, Map1, "Sede")], [], [new(A, 2, "ether2", 1_000_000_000)], [new(A, n)], new HashSet<string>());
        var d = Assert.Single(p.Devices);
        Assert.Equal(("S3300-52X", DeviceType.Switch, "192.0.2.223"), (d.Name, d.Type, d.Address));
    }

    [Fact]
    public void Bridge_port_name_matches_the_inventory_for_traffic_source()
    {
        var p = Plan([new(A, N("bridge/ether2", "sw1", "10.0.0.2", "1/g24", proto: "lldp"))]);
        Assert.Equal((A, 2), (Assert.Single(p.Links).SourceDeviceId!.Value, p.Links[0].SourceIfIndex!.Value));
    }

    [Theory]
    [InlineData("ether2", "ether2")]
    [InlineData("bridge/ether2", "ether2")]
    [InlineData("Gi1/0/24", "Gi1/0/24")] // i nomi Cisco con la barra restano interi
    [InlineData("bridgeLan/ether2 pc-accettazione", "ether2")] // porta con il commento (RouterOS in LLDP-MIB)
    [InlineData("bridge/ether9", null)]
    public void Interface_lookup_used_by_proposals_and_accept(string announced, string? expected)
    {
        string[] inventory = ["ether1", "ether2", "Gi1/0/24", "0/24"];
        Assert.Equal(expected, FindInterface(inventory.Select(n => new InterfaceInfo(A, 1, n, null)).ToList(), announced, i => i.Name)?.Name);
    }

    [Theory]
    [InlineData("MikroTik", "CRS326-24G-2S+", null, DeviceType.Switch)]
    [InlineData("MikroTik", "CCR2004-1G-12S+2XS", null, DeviceType.Router)]
    [InlineData("MikroTik", "wAP ac", null, DeviceType.AccessPoint)]
    [InlineData("cisco WS-C2960X-48FPD-L", null, "switch", DeviceType.Switch)]
    [InlineData("Cisco IP Phone 8841", null, "telephone", DeviceType.Phone)]
    [InlineData("HPE OfficeConnect", null, "bridge", DeviceType.Switch)]
    [InlineData("UniFi", null, "wlan-ap,bridge", DeviceType.AccessPoint)]
    [InlineData("Linux", null, "station", DeviceType.Pc)]
    public void Type_guess(string platform, string? board, string? caps, DeviceType expected) =>
        Assert.Equal(expected, GuessType(N("x", "x", null, null, platform, board, caps)));
}

public class DiscoveryArpScanTests
{
    private static readonly Guid R = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Map1 = Guid.NewGuid(), Customer = Guid.NewGuid(), ScanMap = Guid.NewGuid();
    private static readonly DiscoveryPlanner.DeviceInfo Router = new(R, "gw-sede", "10.0.0.1", Customer, HasInterfaces: false);

    private static (List<DiscoveryDeviceProposalDto> Devices, List<DiscoveryLinkProposalDto> Links, int Ignored) Plan(
        IReadOnlyList<DiscoveryPlanner.NeighborInfo>? neighbors = null, IReadOnlyList<DiscoveryPlanner.ArpInfo>? arp = null,
        IReadOnlyList<DiscoveryPlanner.ScanHostInfo>? scans = null) =>
        DiscoveryPlanner.Plan([Router], [new(R, Map1, "Sede")], [], [], neighbors ?? [], new HashSet<string>(), arp, scans);

    private static ScanHostDto Host(string ip, string? sysName = null, string? sysDescr = null, params int[] ports) =>
        new(ip, 1.2, null, sysName, sysDescr, null, false, ports);

    [Fact]
    public void Lldp_arp_and_scan_of_the_same_host_are_one_proposal()
    {
        // Telefono che via LLDP annuncia solo il MAC: l'IP arriva dall'ARP, il modello dalla scansione
        var lldp = new NeighborDto("lldp", "ether2", null, "W80B", null, "C4:FC:22:00:00:10", null, null, null, null, "telephone");
        var p = Plan(
            neighbors: [new(R, lldp)],
            arp: [new(R, new ArpEntryDto("10.0.0.50", "C4:FC:22:00:00:10", "bridge", null, "W80B-dect", "centralino"))],
            scans: [new(Guid.NewGuid(), null, null, Host("10.0.0.50", sysDescr: "Yealink W80B DECT", ports: 80))]);
        var d = Assert.Single(p.Devices);
        Assert.Equal(("W80B", "10.0.0.50", "C4:FC:22:00:00:10", DeviceType.Phone, "vicini,arp,scansione"), (d.Name, d.Address, d.MacAddress, d.Type, d.Sources));
        Assert.Null(d.Problem);
    }

    [Fact]
    public void Arp_host_is_named_from_dhcp_and_suggested_from_the_router()
    {
        var d = Assert.Single(Plan(arp: [new(R, new ArpEntryDto("10.0.0.60", "AA:BB:CC:00:00:01", "bridge", null, "DESKTOP-ABC", null))]).Devices);
        Assert.Equal(("DESKTOP-ABC", Customer, Map1, R, "arp"), (d.Name, d.SuggestedCustomerId!.Value, d.SuggestedMapId!.Value, d.SuggestedParentId!.Value, d.Sources));
        Assert.Equal("arp", Assert.Single(d.SeenBy).Protocol);
    }

    [Fact]
    public void Scan_suggests_its_own_map_and_snmp_profile()
    {
        var profile = Guid.NewGuid();
        var d = Assert.Single(Plan(scans: [new(Guid.NewGuid(), Customer, ScanMap,
            new ScanHostDto("10.0.0.70", 2, "sw-piano2.ufficio.local", "sw-piano2", "HPE OfficeConnect Switch 1920S", profile, false, [22, 80]))]).Devices);
        Assert.Equal(("sw-piano2", DeviceType.Switch, ScanMap, true, profile), (d.Name, d.Type, d.SuggestedMapId!.Value, d.Snmp, d.SnmpProfileId!.Value));
    }

    [Fact]
    public void Printer_without_sysname_is_named_from_sysdescr()
    {
        var d = Assert.Single(Plan(scans: [new(Guid.NewGuid(), null, null, Host("192.0.2.57", sysDescr: "UTAX_TA Printing System", ports: [80, 9100]))]).Devices);
        Assert.Equal(("UTAX_TA", DeviceType.Printer), (d.Name, d.Type));
        Assert.Equal("192.0.2.3", Assert.Single(Plan(scans: [new(Guid.NewGuid(), null, null, Host("192.0.2.3", sysDescr: "Linux host 6.1"))]).Devices).Name);
    }

    [Fact]
    public void Existing_devices_are_not_proposed_again() =>
        Assert.Empty(Plan(arp: [new(R, new ArpEntryDto("10.0.0.1", "AA:BB:CC:00:00:09", null, null, null, null))],
            scans: [new(Guid.NewGuid(), null, null, Host("10.0.0.1"))]).Devices);

    // Due sedi con la stessa rete (192.0.2.0/24): stesso IP, MAC diversi
    private static readonly Guid R2 = Guid.Parse("00000000-0000-0000-0000-0000000000a2"), Other = Guid.NewGuid();
    private static readonly DiscoveryPlanner.DeviceInfo Router2 = new(R2, "gw-altra-sede", "10.9.0.1", Other, HasInterfaces: false);

    private static List<DiscoveryDeviceProposalDto> TwoSites(IReadOnlyList<DiscoveryPlanner.ArpInfo> arp,
        IReadOnlyList<DiscoveryPlanner.ScanHostInfo>? scans = null, IReadOnlySet<string>? ignored = null, IReadOnlyList<DiscoveryPlanner.DeviceInfo>? more = null) =>
        DiscoveryPlanner.Plan([Router, Router2, .. more ?? []], [], [], [], [], ignored ?? new HashSet<string>(), arp, scans).Devices;

    [Fact]
    public void Same_ip_with_different_macs_at_two_sites_are_two_hosts()
    {
        var devices = TwoSites([
            new(R, new ArpEntryDto("192.0.2.49", "C4:FC:22:00:00:01", "bridge", null, "telefono", null)),
            new(R2, new ArpEntryDto("192.0.2.49", "00:0C:29:00:00:02", "bridge", null, "server", null))]);
        Assert.Equal(2, devices.Count);
        Assert.Equal(["dev:192.0.2.49|00:0c:29:00:00:02", "dev:192.0.2.49|c4:fc:22:00:00:01"], devices.Select(d => d.Key).Order());
        Assert.All(devices, d => Assert.Single(d.SeenBy));
        Assert.Equal(Other, devices.Single(d => d.Name == "server").SuggestedCustomerId);
    }

    [Fact]
    public void Same_ip_and_mac_from_two_routers_is_one_host() =>
        Assert.Equal(2, Assert.Single(TwoSites([
            new(R, new ArpEntryDto("192.0.2.8", "90:E6:BA:00:00:11", "bridge", null, null, null)),
            new(R2, new ArpEntryDto("192.0.2.8", "90:e6:ba:00:00:11", "ether1", null, null, null))])).SeenBy.Count);

    [Fact]
    public void Scan_joins_only_the_host_of_its_customer()
    {
        var arp = new DiscoveryPlanner.ArpInfo[]
        {
            new(R, new ArpEntryDto("192.0.2.49", "C4:FC:22:00:00:01", null, null, null, null)),
            new(R2, new ArpEntryDto("192.0.2.49", "00:0C:29:00:00:02", null, null, null, null))
        };
        var mine = TwoSites(arp, [new(Guid.NewGuid(), Other, null, Host("192.0.2.49", ports: 443))]);
        Assert.Equal("arp,scansione", mine.Single(d => d.MacAddress == "00:0C:29:00:00:02").Sources);

        // Scansione senza cliente: non si sa a quale sede appartenga, resta a parte
        var unknown = TwoSites(arp, [new(Guid.NewGuid(), null, null, Host("192.0.2.49", ports: 443))]);
        Assert.Equal(3, unknown.Count);
        Assert.Equal("dev:192.0.2.49|-", unknown.Single(d => d.Sources == "scansione").Key);
    }

    [Fact]
    public void Old_ignore_by_ip_still_hides_the_split_hosts()
    {
        var arp = new DiscoveryPlanner.ArpInfo[]
        {
            new(R, new ArpEntryDto("192.0.2.49", "C4:FC:22:00:00:01", null, null, null, null)),
            new(R2, new ArpEntryDto("192.0.2.49", "00:0C:29:00:00:02", null, null, null, null))
        };
        Assert.Empty(TwoSites(arp, ignored: new HashSet<string> { "dev:192.0.2.49" }));
        Assert.Single(TwoSites(arp, ignored: new HashSet<string> { "dev:192.0.2.49|c4:fc:22:00:00:01" }));
    }

    [Fact]
    public void Device_of_another_customer_with_the_same_ip_does_not_hide_the_host()
    {
        var printer = new DiscoveryPlanner.DeviceInfo(Guid.NewGuid(), "stampante sede 1", "192.0.2.57", Customer, HasInterfaces: false);
        var d = Assert.Single(TwoSites([
            new(R, new ArpEntryDto("192.0.2.57", "00:C0:EE:00:00:01", null, null, null, null)),
            new(R2, new ArpEntryDto("192.0.2.57", "00:C0:EE:00:00:02", null, null, null, null))], more: [printer]));
        Assert.Equal(R2, Assert.Single(d.SeenBy).DeviceId);
    }

    [Theory]
    [InlineData("RouterOS CCR2004-1G-12S+2XS", new int[0], DeviceType.Router)]
    [InlineData(null, new[] { 9100, 80 }, DeviceType.Printer)]
    [InlineData("HP LaserJet MFP M428", new[] { 80 }, DeviceType.Printer)]
    [InlineData(null, new[] { 554, 80 }, DeviceType.Camera)]
    [InlineData("Linux NAS DS920+ Synology", new int[0], DeviceType.Storage)]
    [InlineData(null, new[] { 3389 }, DeviceType.Pc)]
    [InlineData("VMware ESXi 8.0.2", new[] { 443 }, DeviceType.Server)]
    [InlineData(null, new[] { 8728 }, DeviceType.Router)]
    [InlineData(null, new[] { 22, 80 }, DeviceType.Other)]
    public void Host_type_from_sysdescr_and_ports(string? sysDescr, int[] ports, DeviceType expected) =>
        Assert.Equal(expected, DiscoveryPlanner.GuessHostType(sysDescr, ports, []));

    [Fact]
    public void Routeros_arp_joined_with_dhcp_leases()
    {
        var arp = NeighborParsers.FromRouterOsArp(
        [
            new Dictionary<string, string> { ["address"] = "10.0.0.10", ["mac-address"] = "aa:bb:cc:dd:ee:01", ["interface"] = "bridge", ["dynamic"] = "true" },
            new Dictionary<string, string> { ["address"] = "10.0.0.11", ["interface"] = "bridge" },                       // incompleta
            new Dictionary<string, string> { ["address"] = "10.0.0.12", ["mac-address"] = "aa:bb:cc:dd:ee:03", ["disabled"] = "true" }
        ],
        [new Dictionary<string, string> { ["mac-address"] = "AA:BB:CC:DD:EE:01", ["host-name"] = "NPI000001", ["comment"] = "Stampante amministrazione" }]);
        var e = Assert.Single(arp);
        Assert.Equal(("10.0.0.10", "AA:BB:CC:DD:EE:01", "NPI000001", "Stampante amministrazione"), (e.Address, e.MacAddress, e.HostName, e.Comment));
    }

    [Fact]
    public void Snmp_arp_table()
    {
        var phys = new List<(uint[], Lextm.SharpSnmpLib.ISnmpData)>
        {
            ([3, 10, 0, 0, 20], new Lextm.SharpSnmpLib.OctetString([0x00, 0x11, 0x22, 0x33, 0x44, 0x55])),
            ([3, 10, 0, 0, 21], new Lextm.SharpSnmpLib.OctetString([0x00, 0x11, 0x22, 0x33, 0x44, 0x56]))
        };
        var types = new List<(uint[], Lextm.SharpSnmpLib.ISnmpData)> { ([3, 10, 0, 0, 21], new Lextm.SharpSnmpLib.Integer32(2)) }; // invalid
        var e = Assert.Single(NeighborParsers.FromSnmpArp(phys, types));
        Assert.Equal(("10.0.0.20", "00:11:22:33:44:55", 3), (e.Address, e.MacAddress, e.IfIndex!.Value));
    }

    [Theory]
    [InlineData("198.51.100.0/24", "198.51.100.0/24")]
    [InlineData("198.51.100.77/24", "198.51.100.0/24")]
    [InlineData("10.1.4.0/22", "10.1.4.0/22")]
    [InlineData("10.0.0.0/16", null)]
    [InlineData("fe80::/64", null)]
    [InlineData("pippo", null)]
    public void Cidr_validation(string text, string? expected) => Assert.Equal(expected, VedettaVip.Api.Endpoints.DiscoveryEndpoints.ParseCidr(text));

    [Fact]
    public void Cidr_hosts_exclude_network_and_broadcast()
    {
        Assert.True(Cidr.TryParse("192.0.2.0/30", out var net, out var prefix));
        Assert.Equal(["192.0.2.1", "192.0.2.2"], Cidr.Hosts(net, prefix).Select(i => i.ToString()));
        Assert.True(Cidr.TryParse("10.0.0.0/22", out net, out prefix));
        Assert.Equal(1022, Cidr.Hosts(net, prefix).Count());
    }
}
