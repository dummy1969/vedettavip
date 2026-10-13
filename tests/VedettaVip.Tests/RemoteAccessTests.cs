// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Api.Endpoints;
using VedettaVip.Api.Services;
using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;

namespace VedettaVip.Tests;

/// <summary>Accessi di gestione: link web e PuTTY, porte effettive, produttori dal nome e colonne CSV.</summary>
public class RemoteAccessTests
{
    [Theory]
    [InlineData("192.0.2.1", WebAccess.Http, null, "http://192.0.2.1/")]
    [InlineData("192.0.2.1", WebAccess.Http, 80, "http://192.0.2.1/")]
    [InlineData("192.0.2.1", WebAccess.Https, 443, "https://192.0.2.1/")]
    [InlineData(" router.example.com ", WebAccess.Https, 8443, "https://router.example.com:8443/")]
    [InlineData("192.0.2.1", WebAccess.Http, 8080, "http://192.0.2.1:8080/")]
    [InlineData("2001:db8::1", WebAccess.Https, 4443, "https://[2001:db8::1]:4443/")]
    [InlineData("192.0.2.1", WebAccess.None, 80, null)]
    [InlineData("", WebAccess.Http, null, null)]
    [InlineData("host/../x", WebAccess.Http, null, null)]
    [InlineData("a b", WebAccess.Http, null, null)]
    public void Web_url(string address, WebAccess web, int? port, string? expected) =>
        Assert.Equal(expected, RemoteAccess.WebUrl(address, web, port));

    [Theory]
    [InlineData("192.0.2.1", TerminalAccess.Ssh, null, "vedettavip-ssh://192.0.2.1")]
    [InlineData("192.0.2.1", TerminalAccess.Ssh, 22, "vedettavip-ssh://192.0.2.1")]
    [InlineData("192.0.2.1", TerminalAccess.Ssh, 2222, "vedettavip-ssh://192.0.2.1:2222")]
    [InlineData("router.example.com", TerminalAccess.Telnet, 23, "vedettavip-telnet://router.example.com")]
    [InlineData("router.example.com", TerminalAccess.Telnet, 2323, "vedettavip-telnet://router.example.com:2323")]
    [InlineData("2001:db8::1", TerminalAccess.Ssh, 2222, "vedettavip-ssh://[2001:db8::1]:2222")]
    [InlineData("192.0.2.1", TerminalAccess.None, 22, null)]
    [InlineData("-oProxyCommand=id", TerminalAccess.Ssh, null, null)]
    [InlineData("user@host", TerminalAccess.Ssh, null, null)]
    public void Terminal_href(string address, TerminalAccess terminal, int? port, string? expected) =>
        Assert.Equal(expected, RemoteAccess.TerminalHref(address, terminal, port));

    [Fact]
    public void Device_port_wins_over_the_general_one_of_its_protocol()
    {
        var settings = new RemoteAccessSettingsDto(null, null, WinBoxPort: 18291, HttpPort: 8080, HttpsPort: 8443, SshPort: 2222, TelnetPort: 2323);

        Assert.Equal(new DeviceAccessDto(18291, WebAccess.Https, 8443, TerminalAccess.Ssh, 2222),
            RemoteAccess.Resolve(null, WebAccess.Https, null, TerminalAccess.Ssh, null, settings));
        Assert.Equal(new DeviceAccessDto(8291, WebAccess.Http, 81, TerminalAccess.Telnet, 2323),
            RemoteAccess.Resolve(8291, WebAccess.Http, 81, TerminalAccess.Telnet, null, settings));
        // Nessun accesso: nessuna porta, anche se il device ne aveva una
        Assert.Equal(new DeviceAccessDto(null, WebAccess.None, null, TerminalAccess.None, null),
            RemoteAccess.Resolve(null, WebAccess.None, 8080, TerminalAccess.None, 22, RemoteAccessSettingsDto.Default));
    }

    /// <summary>
    /// Nodi della mappa: la query legge le porte del dispositivo così come sono, le generali si aggiungono in memoria
    /// (in SQL, con le generali null, PostgreSQL falliva su tutte le mappe: COALESCE integer/text).
    /// </summary>
    [Fact]
    public void Map_node_ports_are_resolved_after_the_query()
    {
        var node = new MapNodeDto(Guid.CreateVersion7(), MapNodeKind.Device, Guid.CreateVersion7(), null, 0, 0, "[Name]", null, "r1",
            "192.0.2.1", DeviceType.Router, NodeState.Up, Access: new DeviceAccessDto(null, WebAccess.Https, null, TerminalAccess.Ssh, 2200));
        var settings = new RemoteAccessSettingsDto(null, null, WinBoxPort: 18291, HttpsPort: 8443);

        Assert.Equal(new DeviceAccessDto(18291, WebAccess.Https, 8443, TerminalAccess.Ssh, 2200), MapEndpoints.WithAccess(node, settings).Access);
        Assert.Equal(new DeviceAccessDto(null, WebAccess.Https, null, TerminalAccess.Ssh, 2200),
            MapEndpoints.WithAccess(node, RemoteAccessSettingsDto.Default).Access);
        var submap = node with { Kind = MapNodeKind.Submap, Access = null };
        Assert.Null(MapEndpoints.WithAccess(submap, settings).Access);
    }

    [Theory]
    [InlineData("MikroTik", DeviceVendor.MikroTik)]
    [InlineData("Cisco Systems", DeviceVendor.Cisco)]
    [InlineData("DrayTek", DeviceVendor.DrayTek)]
    [InlineData("AVM (Fritz!Box)", DeviceVendor.Fritz)]
    [InlineData("TP-Link", DeviceVendor.TpLink)]
    [InlineData("Yealink", DeviceVendor.Generic)]
    [InlineData(null, DeviceVendor.Generic)]
    public void Vendor_from_the_mac_vendor_name(string? name, DeviceVendor expected) =>
        Assert.Equal(expected, DiscoveryPlanner.VendorFromName(name));

    [Theory]
    [InlineData("cisco", DeviceVendor.Cisco)]
    [InlineData("DrayTek", DeviceVendor.DrayTek)]
    [InlineData("Vigor", DeviceVendor.DrayTek)]
    [InlineData("FRITZ!Box", DeviceVendor.Fritz)]
    [InlineData("AVM", DeviceVendor.Fritz)]
    [InlineData("TP-Link", DeviceVendor.TpLink)]
    public void Csv_reads_the_new_vendors(string text, DeviceVendor expected)
    {
        Assert.True(DeviceCsv.TryParseVendor(text, out var vendor));
        Assert.Equal(expected, vendor);
        Assert.True(DeviceCsv.TryParseVendor(DeviceCsv.VendorText(expected), out var again));
        Assert.Equal(expected, again);
    }

    [Theory]
    [InlineData("", WebAccess.None)]
    [InlineData("nessuno", WebAccess.None)]
    [InlineData("HTTP", WebAccess.Http)]
    [InlineData("https", WebAccess.Https)]
    public void Csv_web_access(string text, WebAccess expected)
    {
        Assert.True(DeviceCsv.TryParseWeb(text, out var web));
        Assert.Equal(expected, web);
    }

    [Theory]
    [InlineData("", TerminalAccess.None)]
    [InlineData("SSH", TerminalAccess.Ssh)]
    [InlineData("putty", TerminalAccess.Ssh)]
    [InlineData("telnet", TerminalAccess.Telnet)]
    public void Csv_terminal_access(string text, TerminalAccess expected)
    {
        Assert.True(DeviceCsv.TryParseTerminal(text, out var terminal));
        Assert.Equal(expected, terminal);
    }

    [Fact]
    public void Csv_rejects_unknown_accesses()
    {
        Assert.False(DeviceCsv.TryParseWeb("ftp", out _));
        Assert.False(DeviceCsv.TryParseTerminal("rdp", out _));
    }

    [Theory]
    [InlineData("Accesso web", "web")]
    [InlineData("porta_web", "web_port")]
    [InlineData("Terminale", "terminal")]
    [InlineData("Porta terminale", "terminal_port")]
    public void Csv_access_column_aliases(string header, string expected) => Assert.Equal(expected, DeviceCsv.Canonical(header));
}
