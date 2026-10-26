// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Shared.Models;

namespace VedettaVip.Tests;

/// <summary>Dimensione dei nodi e "Metti in colonna le sottomappe".</summary>
public class MapLayoutTests
{
    private static readonly IComparer<string> Names = StringComparer.Create(new System.Globalization.CultureInfo("it-IT"), ignoreCase: true);

    [Fact]
    public void Node_size_follows_the_longest_line_and_the_icon()
    {
        Assert.Equal((100d, 42d), MapLayout.NodeSize(["Bologna", "5 up"], hasIcon: true));       // minimo 70 + icona 30
        Assert.Equal((176d, 42d), MapLayout.NodeSize(["Ferrara", "nessun dispositivo"], true)); // 18 × 7 + 20 + 30
        Assert.Equal((106d, 32d), MapLayout.NodeSize(["Internet"], hasIcon: true));             // l'icona alza il nodo a 32
        Assert.Equal((70d, 27d), MapLayout.NodeSize(["x"], hasIcon: false));
    }

    [Fact]
    public void Column_is_alphabetical_top_down_and_left_aligned()
    {
        // Sottomappe sparse come nello screenshot: larghezze diverse (100 = "N up", 176 = "nessun dispositivo")
        var bologna = Item("Bologna", 140, 50, 100);
        var ferrara = Item("Ferrara", 180, 110, 176);
        var reggio = Item("Reggio Emilia", 460, 410, 142);
        var ravenna = Item("Ravenna", 320, 270, 100);
        var modena = Item("Modena", 160, 350, 100);

        var result = MapLayout.Column([reggio, ferrara, ravenna, bologna, modena], Names);

        Assert.Equal([bologna.Id, ferrara.Id, modena.Id, ravenna.Id, reggio.Id], result.Select(p => p.Id));
        // Bordo sinistro comune: quello del nodo più a sinistra (Bologna, 140 - 50 = 90); partenza dal centro più in alto (50)
        foreach (var (p, item) in result.Zip(new[] { bologna, ferrara, modena, ravenna, reggio }))
            Assert.Equal(90d, p.X - item.Width / 2);
        Assert.Equal([50d, 110d, 170d, 230d, 290d], result.Select(p => p.Y));
    }

    [Fact]
    public void Order_ignores_case_and_accents_and_is_stable_for_equal_names()
    {
        var first = Item("Ravenna", 0, 0, 100, Guid.Parse("00000000-0000-7000-8000-000000000001"));
        var second = Item("ravenna", 0, 100, 176, Guid.Parse("00000000-0000-7000-8000-000000000002"));
        var accented = Item("Àosta", 0, 200, 100);
        var zeta = Item("zeta", 0, 300, 100);

        var result = MapLayout.Column([zeta, second, accented, first], Names);
        Assert.Equal([accented.Id, first.Id, second.Id, zeta.Id], result.Select(p => p.Id));

        // Stesso risultato al secondo clic, partendo dalla colonna già fatta
        var again = MapLayout.Column(
            [.. result.Select(p => new[] { first, second, accented, zeta }.Single(i => i.Id == p.Id) with { X = p.X, Y = p.Y })], Names);
        Assert.Equal(result, again);
    }

    [Fact]
    public void Empty_input_gives_no_placements() => Assert.Empty(MapLayout.Column([], Names));

    private static ColumnItem Item(string name, double x, double y, double width, Guid? id = null) =>
        new(id ?? Guid.CreateVersion7(), name, x, y, width);
}
