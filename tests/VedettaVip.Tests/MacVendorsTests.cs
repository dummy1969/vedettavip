// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Api.Services;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Tests;

public class MacVendorsTests
{
    [Theory]
    [InlineData("00:0C:29:00:00:01", "VMware")]
    [InlineData("bc-24-11-00-00-02", "Proxmox Server")]
    [InlineData("C4:FC:22:00:00:10", "Yealink")]
    [InlineData("0C:38:3E:00:00:03", "Fanvil")]
    [InlineData("00:C0:EE:00:00:04", "Kyocera Display")]
    [InlineData("DC:2C:6E:00:00:05", "MikroTik")]
    [InlineData("dc2c.6ee2.2316", "MikroTik")]
    public void Real_macs_from_the_network(string mac, string vendor) => Assert.Equal(vendor, MacVendors.Lookup(mac));

    [Theory]
    [InlineData("DA:A1:19:00:00:01")] // bit "amministrato localmente": Wi-Fi casuale
    [InlineData("02:00:00:00:00:01")]
    public void Locally_administered_macs_are_private(string mac) => Assert.Equal(MacVendors.PrivateMac, MacVendors.Lookup(mac));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pippo")]
    public void Not_a_mac(string? mac) => Assert.Null(MacVendors.Lookup(mac));

    [Theory]
    [InlineData("YEALINK(XIAMEN) NETWORK TECHNOLOGY CO.,LTD.", "Yealink")]
    [InlineData("VMware, Inc.", "VMware")]
    [InlineData("Hangzhou Hikvision Digital Technology Co.,Ltd.", "Hikvision")]
    [InlineData("Routerboard.com", "MikroTik")]
    [InlineData("Hewlett Packard", "HP")]
    [InlineData("Cisco Systems, Inc", "Cisco")]
    [InlineData("AVM Audiovisuelles Marketing und Computersysteme GmbH", "AVM (Fritz!Box)")]
    [InlineData("ASUSTek COMPUTER INC.", "ASUS")]
    [InlineData("Micro-Star INTL CO., LTD.", "MSI")]
    [InlineData("Intel Corporate", "Intel")]
    [InlineData("NETGEAR", "Netgear")]
    [InlineData("zte corporation", "zte")]
    public void Short_names(string organization, string expected) => Assert.Equal(expected, MacVendors.ShortName(organization));

    [Theory]
    [InlineData("Hikvision", DeviceType.Camera)]
    [InlineData("Yealink", DeviceType.Phone)]
    [InlineData("Kyocera Display", DeviceType.Printer)]
    [InlineData("VMware", DeviceType.Server)]
    [InlineData("Apple", DeviceType.Other)]
    [InlineData(MacVendors.PrivateMac, DeviceType.Other)]
    public void Type_from_vendor(string vendor, DeviceType expected) => Assert.Equal(expected, DiscoveryPlanner.VendorType(vendor, []));

    [Fact]
    public void Arp_host_without_names_gets_vendor_in_name_and_type()
    {
        var r = Guid.NewGuid();
        var p = DiscoveryPlanner.Plan([new(r, "gw", "10.0.0.1", null, false)], [], [], [], [], new HashSet<string>(),
            [new(r, new ArpEntryDto("10.0.0.34", "C4:FC:22:00:00:10", "bridge", null, null, null))]);
        var d = Assert.Single(p.Devices);
        Assert.Equal(("Yealink 10.0.0.34", DeviceType.Phone, "Yealink"), (d.Name, d.Type, d.Vendor));
    }
}
