// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Api.Services;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Tests;

public class DeviceCsvTests
{
    [Fact]
    public void Reads_italian_excel_csv_with_semicolons_bom_and_aliases()
    {
        var table = DeviceCsv.Parse("﻿Nome;Indirizzo IP;Tipo;Versione SNMP;Cliente;Profilo SNMP;Abilitato;Note\r\n" +
                                    "Router sede;10.0.0.1;router;v2c;Rossi Srl;Rossi;sì;primo piano\r\n");

        Assert.Equal([DeviceCsv.Name, DeviceCsv.Address, DeviceCsv.Type, DeviceCsv.Snmp, DeviceCsv.Customer, DeviceCsv.SnmpProfile, DeviceCsv.Enabled],
            table.Columns);
        Assert.Equal(["Note"], table.Ignored);
        var row = Assert.Single(table.Rows);
        Assert.Equal((2, "Router sede", "10.0.0.1", "Rossi Srl"), (row.Line, row.Get(DeviceCsv.Name), row.Get(DeviceCsv.Address), row.Get(DeviceCsv.Customer)));
        Assert.False(row.Has(DeviceCsv.Parent));
    }

    [Fact]
    public void Detects_comma_and_tab_and_handles_quotes_and_line_numbers()
    {
        var table = DeviceCsv.Parse("name,address,customer\n\n# commento\n\"Switch, piano 1\",sw1.local,\"Bianchi \"\"&\"\" C.\"\n");
        var row = Assert.Single(table.Rows);
        Assert.Equal((4, "Switch, piano 1", "Bianchi \"&\" C."), (row.Line, row.Get(DeviceCsv.Name), row.Get(DeviceCsv.Customer)));

        var tabs = DeviceCsv.Parse("name\taddress\nap\t10.0.0.9");
        Assert.Equal("10.0.0.9", Assert.Single(tabs.Rows).Get(DeviceCsv.Address));
    }

    [Fact]
    public void Quoted_field_with_newline_keeps_the_following_line_numbers()
    {
        var table = DeviceCsv.Parse("name;address\n\"due\nrighe\";10.0.0.1\nterzo;10.0.0.3\n");
        Assert.Equal([2, 4], table.Rows.Select(r => r.Line));
        Assert.Equal("due\nrighe", table.Rows[0].Get(DeviceCsv.Name));
    }

    [Fact]
    public void Missing_cells_are_empty()
    {
        var row = Assert.Single(DeviceCsv.Parse("name;address;type\nsolo-nome;10.0.0.1\n").Rows);
        Assert.Equal("", row.Get(DeviceCsv.Type));
        Assert.True(row.Has(DeviceCsv.Type));
    }

    [Theory]
    [InlineData("")]
    [InlineData("# solo commenti\n")]
    [InlineData("nome;tipo\nx;router")]
    [InlineData("name;address;nome\nx;1.1.1.1;y")]
    public void Rejects_files_without_a_usable_header(string csv) =>
        Assert.Throws<FormatException>(() => DeviceCsv.Parse(csv));

    [Theory]
    [InlineData("Router", DeviceType.Router)]
    [InlineData("Access Point", DeviceType.AccessPoint)]
    [InlineData("AP", DeviceType.AccessPoint)]
    [InlineData("", DeviceType.Other)]
    [InlineData("altro", DeviceType.Other)]
    public void Parses_device_types(string text, DeviceType expected)
    {
        Assert.True(DeviceCsv.TryParseType(text, out var type));
        Assert.Equal(expected, type);
    }

    [Theory]
    [InlineData("v2c", SnmpVersion.V2c)]
    [InlineData("2", SnmpVersion.V2c)]
    [InlineData("V1", SnmpVersion.V1)]
    [InlineData("nessuno", SnmpVersion.None)]
    [InlineData("", SnmpVersion.None)]
    public void Parses_snmp_versions(string text, SnmpVersion expected)
    {
        Assert.True(DeviceCsv.TryParseSnmp(text, out var version));
        Assert.Equal(expected, version);
    }

    [Fact]
    public void Rejects_unknown_values()
    {
        Assert.False(DeviceCsv.TryParseType("tostapane", out _));
        Assert.False(DeviceCsv.TryParseSnmp("v4", out _));
        Assert.False(DeviceCsv.TryParseBool("forse", out _));
        Assert.True(DeviceCsv.TryParseBool("No", out var no) && !no);
        Assert.True(DeviceCsv.TryParseBool("", out var empty) && empty);
    }

    [Fact]
    public void Export_round_trips_through_the_parser()
    {
        var csv = DeviceCsv.Write([["Core; sede", "10.0.0.1", "router", "v2c", "Rossi \"A\"", "", "Rossi", "si"]]);
        var row = Assert.Single(DeviceCsv.Parse(csv).Rows);
        Assert.Equal(("Core; sede", "Rossi \"A\"", "si"), (row.Get(DeviceCsv.Name), row.Get(DeviceCsv.Customer), row.Get(DeviceCsv.Enabled)));
    }
}
