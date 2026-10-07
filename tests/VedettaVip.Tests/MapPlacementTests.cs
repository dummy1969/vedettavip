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
}
