// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

namespace VedettaVip.Api.Data.Entities;

/// <summary>
/// Immagine di sfondo di una mappa (planimetria, cartina), una per mappa, nel database: il backup notturno la comprende.
/// Solo PNG, JPEG o WebP riconosciuti dal contenuto (<see cref="Services.ImageInfo"/>), mai SVG (può contenere script).
/// </summary>
public sealed class MapBackground
{
    public const int MaxBytes = 5 * 1024 * 1024;

    public Guid MapId { get; set; }
    public required string ContentType { get; set; }
    public required byte[] Data { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    /// <summary>Orologio dell'API: versione nell'URL dell'immagine (cache del browser).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
