// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

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
    public string? BackgroundImage { get; set; }
    public int GridSize { get; set; } = 20;

    public List<MapNode> Nodes { get; set; } = [];
    public List<MapLink> Links { get; set; } = [];
}
