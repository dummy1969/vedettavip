// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Api.Services;

namespace VedettaVip.Tests;

public class MapPlacementTests
{
    [Fact]
    public void Empty_map_starts_top_left() => Assert.Equal((100d, 100d), MapPlacement.NextBelow([], 20));

    [Fact]
    public void Goes_below_the_lowest_hand_placed_node()
    {
        // Topologia disegnata a mano: il nuovo nodo non va nei buchi, ma sotto il nodo più basso (y 520 + 130)
        (double, double)[] layout = [(400, 160), (860, 200), (760, 520), (980, 480)];
        Assert.Equal((100d, 660d), MapPlacement.NextBelow(layout, 20));
    }

    [Fact]
    public void Continues_the_arrival_row_then_opens_a_new_one()
    {
        (double, double)[] layout = [(400, 160)];
        var first = MapPlacement.NextBelow(layout, 20);
        var second = MapPlacement.NextBelow([.. layout, first], 20);
        Assert.Equal((100d, 300d), first);
        Assert.Equal((280d, 300d), second); // 270 allineato alla griglia da 20

        var fullRow = Enumerable.Range(0, MapPlacement.Columns).Select(i => MapPlacement.Cell(i, 300, 20)).ToList();
        Assert.Equal((100d, 440d), MapPlacement.NextBelow([.. layout, .. fullRow], 20));
    }

    [Fact]
    public void A_moved_arrival_node_starts_a_new_row()
    {
        (double, double)[] layout = [(400, 160), (100, 300), (290, 310)]; // il secondo nuovo nodo è stato spostato
        Assert.Equal((100d, 440d), MapPlacement.NextBelow(layout, 20));
    }

    [Fact]
    public void Positions_are_aligned_to_the_map_grid() =>
        Assert.Equal((275d, 100d), MapPlacement.Cell(1, 100, 25)); // 270 → multiplo di 25 più vicino

    [Fact]
    public void Chosen_point_is_used_aligned_to_the_grid()
    {
        (double, double)[] layout = [(400, 160)];
        Assert.Equal((520d, 240d), MapPlacement.At(527, 233, layout, 20));
        Assert.Equal((-40d, 0d), MapPlacement.At(-37.5, 4, layout, 20));
    }

    [Fact]
    public void Without_a_point_goes_below_the_existing_nodes()
    {
        (double, double)[] layout = [(400, 160)];
        Assert.Equal(MapPlacement.NextBelow(layout, 20), MapPlacement.At(null, null, layout, 20));
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(10d, 20d, true)]
    [InlineData(-1_000_000d, 1_000_000d, true)]
    [InlineData(10d, null, false)]
    [InlineData(null, 20d, false)]
    [InlineData(1_000_001d, 0d, false)]
    [InlineData(0d, double.NaN, false)]
    [InlineData(double.PositiveInfinity, 0d, false)]
    public void Point_needs_both_finite_coordinates_within_limits(double? x, double? y, bool valid) =>
        Assert.Equal(valid, MapPlacement.ValidatePoint(x, y) is null);
}
