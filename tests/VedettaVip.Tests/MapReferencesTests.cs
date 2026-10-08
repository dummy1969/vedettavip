// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Api.Services;
using static VedettaVip.Api.Services.MapReferences;

namespace VedettaVip.Tests;

public class MapReferencesTests
{
    private static readonly MapInfo Sede = new(Guid.NewGuid(), "Sede principale", null);
    private static readonly MapInfo Nord = new(Guid.NewGuid(), "Filiale Nord", Sede.Id);
    private static readonly MapInfo Clienti = new(Guid.NewGuid(), "Clienti", null);
    private static readonly MapInfo RackSede = new(Guid.NewGuid(), "Rack", Sede.Id);
    private static readonly MapInfo RackNord = new(Guid.NewGuid(), "Rack", Nord.Id);
    private static readonly MapReferences Refs = new([Sede, Nord, Clienti, RackSede, RackNord]);

    [Theory]
    [InlineData("Filiale Nord")]
    [InlineData("  filiale nord ")]
    [InlineData("Sede principale/Filiale Nord")]
    public void Resolves_unique_names_ignoring_case_and_paths(string reference) =>
        Assert.Equal(Nord, Refs.Resolve(reference).Map);

    [Fact]
    public void Ambiguous_name_needs_a_path()
    {
        var (map, error) = Refs.Resolve("Rack");
        Assert.Null(map);
        Assert.Contains("ambigua", error);
        Assert.Contains("Sede principale/", error);

        Assert.Equal(RackSede, Refs.Resolve("Sede principale / Rack").Map);
        Assert.Equal(RackNord, Refs.Resolve("Filiale Nord/Rack").Map); // basta la parte finale del percorso
        Assert.Equal(RackNord, Refs.Resolve("Sede principale/Filiale Nord/Rack").Map);
    }

    [Theory]
    [InlineData("Magazzino")]
    [InlineData("Clienti/Rack")]
    public void Unknown_maps_are_errors(string reference)
    {
        var (map, error) = Refs.Resolve(reference);
        Assert.Null(map);
        Assert.Contains("inesistente", error);
    }

    [Fact]
    public void List_splits_on_pipe_removes_duplicates_and_stops_at_the_first_error()
    {
        var (maps, error) = Refs.ResolveList("Clienti | filiale nord |Clienti");
        Assert.Null(error);
        Assert.Equal([Clienti, Nord], maps);

        Assert.Empty(Refs.ResolveList("").Maps);
        Assert.Contains("Magazzino", Refs.ResolveList("Clienti|Magazzino").Error);
        Assert.Empty(Refs.ResolveList("Clienti|Magazzino").Maps);
    }

    [Fact]
    public void Reference_is_the_name_when_unique_otherwise_the_full_path()
    {
        Assert.Equal("Filiale Nord", Refs.Reference(Nord.Id));
        Assert.Equal("Sede principale/Filiale Nord/Rack", Refs.Reference(RackNord.Id));
        Assert.Equal(RackNord, Refs.Resolve(Refs.Reference(RackNord.Id)).Map);
    }
}
