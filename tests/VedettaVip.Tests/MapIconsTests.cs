// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Api.Services;
using VedettaVip.Shared.Contracts;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Tests;

public class MapIconsTests
{
    [Fact]
    public void Every_device_type_has_a_default_icon_in_the_catalog()
    {
        foreach (var type in Enum.GetValues<DeviceType>())
            Assert.NotNull(MapIcons.Find(MapIcons.DefaultFor(type)));
        Assert.NotNull(MapIcons.Find(MapIcons.DefaultFor(MapNodeKind.Submap, null)));
        Assert.NotNull(MapIcons.Find("cloud")); // usata dalla migration StaticInternetCloudIcon
    }

    [Fact]
    public void Catalog_keys_are_unique_and_bodies_are_svg_shapes()
    {
        Assert.Equal(MapIcons.All.Count, MapIcons.All.Select(i => i.Key).Distinct().Count());
        Assert.All(MapIcons.All, i => Assert.Matches("^<(path|circle|rect|line|polyline|ellipse) ", i.Body));
    }

    [Fact]
    public void Unknown_or_missing_keys_fall_back()
    {
        Assert.Equal("router", MapIcons.Resolve("tolta-dal-catalogo", null, "router"));
        Assert.Null(MapIcons.Resolve(null, "nessuna"));
    }

    [Fact]
    public void Every_device_type_round_trips_through_the_csv()
    {
        foreach (var type in Enum.GetValues<DeviceType>())
        {
            Assert.True(DeviceCsv.TryParseType(DeviceCsv.TypeText(type), out var parsed));
            Assert.Equal(type, parsed);
        }
        Assert.True(DeviceCsv.TryParseType("NAS", out var nas) && nas == DeviceType.Storage);
        Assert.True(DeviceCsv.TryParseType("telecamera", out var cam) && cam == DeviceType.Camera);
    }
}
