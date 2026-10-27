// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Data.Entities;

public sealed class Map
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    /// <summary>
    /// Mappa che contiene il nodo-sottomappa che punta a questa mappa.
    /// Mantenuto dall'API quando crea, sposta o elimina nodi di tipo Submap.
    /// </summary>
    public Guid? ParentMapId { get; set; }
    public Map? ParentMap { get; set; }
    /// <summary>Campo storico mai usato dalla UI (ripassato invariato da Rinomina): lo sfondo è in BackgroundMode e seguenti.</summary>
    public string? BackgroundImage { get; set; }
    public int GridSize { get; set; } = 20;

    /// <summary>Sfondo: nessuno (griglia), immagine (<see cref="Background"/>) o mappa geografica (Geo*).</summary>
    public MapBackgroundMode BackgroundMode { get; set; } = MapBackgroundMode.None;
    /// <summary>Opacità dello sfondo (immagine o tasselli), 10–100.</summary>
    public int BackgroundOpacityPct { get; set; } = 100;
    /// <summary>Scala dell'immagine di sfondo, 1–1000 % (100 = un pixel dell'immagine per unità della mappa).</summary>
    public int BackgroundScalePct { get; set; } = 100;
    /// <summary>Griglia disegnata sopra lo sfondo (senza sfondo la griglia c'è sempre).</summary>
    public bool BackgroundShowGrid { get; set; }
    /// <summary>
    /// Mappa geografica: latitudine e longitudine del punto (0, 0) della mappa e livello di zoom dei tasselli a cui un'unità
    /// della mappa è un pixel (Web Mercator, tasselli da 256 px). I nodi restano in X/Y.
    /// </summary>
    public double? GeoLatitude { get; set; }
    public double? GeoLongitude { get; set; }
    public int? GeoZoom { get; set; }
    public MapBackground? Background { get; set; }

    public List<MapNode> Nodes { get; set; } = [];
    public List<MapLink> Links { get; set; } = [];
}
