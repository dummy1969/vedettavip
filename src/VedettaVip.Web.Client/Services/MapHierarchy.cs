// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Shared.Contracts;

namespace VedettaVip.Web.Client.Services;

/// <summary>Mappe in ordine ad albero per i menu a tendina (selettore della mappa, nuovo dispositivo).</summary>
public static class MapHierarchy
{
    /// <summary>Ogni mappa principale seguita dalle sue sottomappe, con la profondità; nomi in ordine alfabetico.</summary>
    public static IEnumerable<(MapSummaryDto Map, int Depth)> Order(IReadOnlyList<MapSummaryDto> maps)
    {
        var children = maps.Where(m => m.ParentMapId is not null).ToLookup(m => m.ParentMapId!.Value);
        var ids = maps.Select(m => m.Id).ToHashSet();
        var visited = new HashSet<Guid>();

        IEnumerable<(MapSummaryDto, int)> Walk(MapSummaryDto m, int depth)
        {
            if (!visited.Add(m.Id)) yield break;
            yield return (m, depth);
            foreach (var child in children[m.Id].OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
                foreach (var item in Walk(child, depth + 1))
                    yield return item;
        }

        // Radici: senza padre o con un padre che non esiste più
        var roots = maps.Where(m => m.ParentMapId is not { } p || !ids.Contains(p))
                        .OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase);
        foreach (var root in roots)
            foreach (var item in Walk(root, 0))
                yield return item;
    }

    /// <summary>La mappa e tutte le sue discendenti (protetto da cicli).</summary>
    public static HashSet<Guid> Subtree(IReadOnlyList<MapSummaryDto> maps, Guid root)
    {
        var children = maps.Where(m => m.ParentMapId is not null).ToLookup(m => m.ParentMapId!.Value, m => m.Id);
        var result = new HashSet<Guid> { root };
        var queue = new Queue<Guid>([root]);
        while (queue.TryDequeue(out var id))
            foreach (var child in children[id])
                if (result.Add(child))
                    queue.Enqueue(child);
        return result;
    }

    /// <summary>Rientro per le option di un select (gli spazi normali verrebbero compressi).</summary>
    public static string Indent(int depth) => depth == 0 ? "" : new string(' ', depth * 4) + "└ ";
}
