// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

namespace VedettaVip.Shared.Models;

/// <summary>Elemento da incolonnare: nodo con nome, centro attuale e larghezza disegnata.</summary>
public readonly record struct ColumnItem(Guid Id, string Name, double X, double Y, double Width);

/// <summary>Nuovo centro di un nodo.</summary>
public readonly record struct NodePlacement(Guid Id, double X, double Y);

/// <summary>
/// Geometria dei nodi della mappa. Le coordinate di un nodo sono il suo <b>centro</b>; la larghezza dipende dall'etichetta,
/// quindi la conosce solo chi la disegna (il client): la stessa formula serve al disegno e all'incolonnamento.
/// </summary>
public static class MapLayout
{
    public const int IconSize = 22, IconSpace = 30;
    /// <summary>Distanza verticale tra i centri dei nodi incolonnati (nodi a due righe alti 42 px).</summary>
    public const double ColumnStep = 60;

    /// <summary>Riquadro di un nodo: circa 7 px per carattere della riga più lunga, minimo 70, più lo spazio dell'icona.</summary>
    public static (double Width, double Height) NodeSize(IReadOnlyList<string> lines, bool hasIcon)
    {
        var longest = lines.Count == 0 ? 0 : lines.Max(l => l.Length);
        var width = Math.Max(70, longest * 7 + 20) + (hasIcon ? IconSpace : 0);
        var height = Math.Max(lines.Count * 15 + 12, hasIcon ? IconSize + 10 : 0);
        return (width, height);
    }

    /// <summary>
    /// Colonna in ordine alfabetico (dall'alto in basso), allineata a <b>sinistra</b> sul bordo del nodo più a sinistra e
    /// a partire dal centro del nodo più in alto, un nodo ogni <see cref="ColumnStep"/>. A parità di nome decide l'Id
    /// (ordine stabile tra un clic e l'altro).
    /// </summary>
    public static IReadOnlyList<NodePlacement> Column(IReadOnlyList<ColumnItem> items, IComparer<string> nameComparer)
    {
        if (items.Count == 0)
            return [];

        var left = items.Min(i => i.X - i.Width / 2);
        var top = items.Min(i => i.Y);
        return items
            .OrderBy(i => i.Name, nameComparer)
            .ThenBy(i => i.Id)
            .Select((item, index) => new NodePlacement(item.Id, left + item.Width / 2, top + index * ColumnStep))
            .ToList();
    }
}
