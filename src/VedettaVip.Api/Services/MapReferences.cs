// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

namespace VedettaVip.Api.Services;

/// <summary>
/// Riferimenti testuali alle mappe (colonna "mappa" del CSV dei dispositivi). Una mappa si indica con il nome (senza
/// distinzione di maiuscole) oppure, se il nome non è univoco, con il percorso dalla radice o da un antenato
/// ("Sede principale/Filiale Nord"). Più mappe nella stessa cella sono separate da <see cref="ListSeparator"/>.
/// </summary>
public sealed class MapReferences
{
    public const char PathSeparator = '/';
    public const char ListSeparator = '|';

    public sealed record MapInfo(Guid Id, string Name, Guid? ParentMapId);

    private readonly Dictionary<Guid, MapInfo> byId;
    private readonly ILookup<string, MapInfo> byName;

    public MapReferences(IEnumerable<MapInfo> maps)
    {
        byId = maps.ToDictionary(m => m.Id);
        byName = byId.Values.ToLookup(m => m.Name.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    public MapInfo? Find(Guid id) => byId.GetValueOrDefault(id);

    /// <summary>Mappe indicate nella cella (senza ripetizioni); errore alla prima che non si risolve. Cella vuota = nessuna.</summary>
    public (IReadOnlyList<MapInfo> Maps, string? Error) ResolveList(string cell)
    {
        var result = new List<MapInfo>();
        foreach (var reference in cell.Split(ListSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var (map, error) = Resolve(reference);
            if (map is null)
                return ([], error);
            if (!result.Contains(map))
                result.Add(map);
        }
        return (result, null);
    }

    /// <summary>Nome univoco, altrimenti percorso: gli ultimi segmenti devono coincidere con la mappa e i suoi antenati.</summary>
    public (MapInfo? Map, string? Error) Resolve(string reference)
    {
        reference = reference.Trim();
        var named = byName[reference].ToList();
        if (named.Count == 1)
            return (named[0], null);

        var segments = reference.Split(PathSeparator, StringSplitOptions.TrimEntries);
        if (segments.Length > 1 && segments.All(s => s.Length > 0))
        {
            var matches = byName[segments[^1]].Where(m => AncestorsMatch(m, segments)).ToList();
            if (matches.Count == 1)
                return (matches[0], null);
            if (matches.Count > 1)
                return (null, $"mappa \"{reference}\" ambigua ({matches.Count} mappe con questo percorso): indicare il percorso completo");
        }

        return named.Count > 1
            ? (null, $"mappa \"{reference}\" ambigua ({named.Count} mappe con questo nome): usare il percorso, es. \"{Path(named[0])}\"")
            : (null, $"mappa \"{reference}\" inesistente (le mappe si creano dalla pagina Mappa)");
    }

    /// <summary>Riferimento più corto che individua la mappa: il nome se univoco, altrimenti il percorso dalla radice.</summary>
    public string Reference(Guid mapId)
    {
        var map = byId[mapId];
        return byName[map.Name.Trim()].Count() == 1 ? map.Name : Path(map);
    }

    private string Path(MapInfo map)
    {
        var names = new List<string>();
        for (MapInfo? m = map; m is not null && names.Count < 64; m = m.ParentMapId is { } p ? byId.GetValueOrDefault(p) : null)
            names.Add(m.Name.Trim());
        names.Reverse();
        return string.Join(PathSeparator, names);
    }

    private bool AncestorsMatch(MapInfo map, string[] segments)
    {
        MapInfo? current = map;
        for (var i = segments.Length - 1; i >= 0; i--)
        {
            if (current is null || !string.Equals(current.Name.Trim(), segments[i], StringComparison.OrdinalIgnoreCase))
                return false;
            current = current.ParentMapId is { } p ? byId.GetValueOrDefault(p) : null;
        }
        return true;
    }
}
