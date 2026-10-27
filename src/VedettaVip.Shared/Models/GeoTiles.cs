// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Globalization;

namespace VedettaVip.Shared.Models;

/// <summary>Tassello da disegnare: indici (z, x, y) e riquadro in coordinate della mappa.</summary>
public readonly record struct TilePlacement(int Z, int X, int Y, double MapX, double MapY, double Size);

/// <summary>
/// Mappa geografica a tasselli (Web Mercator, tasselli da 256 px, come OpenStreetMap e Google). Una mappa geografica ha
/// un'origine (latitudine e longitudine del punto 0,0) e un livello di zoom di riferimento: a quel livello un'unità della
/// mappa è un pixel del mondo. I nodi restano in X/Y; i tasselli si scelgono in base allo zoom della vista.
/// </summary>
public static class GeoTiles
{
    public const int TileSize = 256;
    public const int MinZoom = 1, MaxZoom = 19;
    /// <summary>Limite di latitudine della proiezione Web Mercator.</summary>
    public const double MaxLatitude = 85.0511;
    /// <summary>Oltre questo numero di tasselli per vista si usa il livello meno dettagliato (vista molto grande).</summary>
    public const int MaxTiles = 200;

    public const string DefaultUrl = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
    public const string DefaultAttribution = "© OpenStreetMap contributors";
    public const string DefaultAttributionUrl = "https://www.openstreetmap.org/copyright";

    /// <summary>Livelli proposti nel pannello dello sfondo.</summary>
    public static readonly (int Zoom, string Label)[] ZoomPresets =
    [
        (6, "Italia"), (8, "Regione"), (10, "Provincia"), (12, "Città"), (14, "Quartiere"), (16, "Strada")
    ];

    /// <summary>Pixel del mondo al livello <paramref name="zoom"/> (origine in alto a sinistra, longitudine -180, latitudine 85).</summary>
    public static (double X, double Y) Project(double latitude, double longitude, int zoom)
    {
        var size = TileSize * Math.Pow(2, zoom);
        var lat = Math.Clamp(latitude, -MaxLatitude, MaxLatitude) * Math.PI / 180;
        var x = (longitude + 180) / 360 * size;
        var y = (1 - Math.Log(Math.Tan(lat) + 1 / Math.Cos(lat)) / Math.PI) / 2 * size;
        return (x, y);
    }

    public static (double Latitude, double Longitude) Unproject(double x, double y, int zoom)
    {
        var size = TileSize * Math.Pow(2, zoom);
        var longitude = x / size * 360 - 180;
        var n = Math.PI - 2 * Math.PI * y / size;
        var latitude = 180 / Math.PI * Math.Atan(Math.Sinh(n));
        return (latitude, longitude);
    }

    /// <summary>Origine (punto 0,0) che porta il punto <paramref name="mapX"/>, <paramref name="mapY"/> della mappa sul centro indicato.</summary>
    public static (double Latitude, double Longitude) OriginFor(double centerLat, double centerLon, int zoom, double mapX, double mapY)
    {
        var (cx, cy) = Project(centerLat, centerLon, zoom);
        return Unproject(cx - mapX, cy - mapY, zoom);
    }

    /// <summary>Coordinate geografiche del punto <paramref name="mapX"/>, <paramref name="mapY"/> della mappa.</summary>
    public static (double Latitude, double Longitude) ToLatLon(double originLat, double originLon, int zoom, double mapX, double mapY)
    {
        var (ox, oy) = Project(originLat, originLon, zoom);
        return Unproject(ox + mapX, oy + mapY, zoom);
    }

    /// <summary>Livello dei tasselli per lo zoom della vista: un tassello resta circa 256 px sullo schermo.</summary>
    public static int TileZoom(int geoZoom, double viewZoom) =>
        Math.Clamp(geoZoom + (int)Math.Round(Math.Log2(viewZoom)), 0, MaxZoom);

    /// <summary>
    /// Tasselli che coprono il riquadro visibile (coordinate della mappa). Se servirebbero più di <see cref="MaxTiles"/>
    /// tasselli si scende di livello; fuori dal mondo in verticale non ce ne sono, in orizzontale il mondo si ripete.
    /// </summary>
    public static IReadOnlyList<TilePlacement> Visible(double originLat, double originLon, int geoZoom,
        double left, double top, double right, double bottom, double viewZoom)
    {
        if (!(right > left && bottom > top) || !double.IsFinite(left + top + right + bottom))
            return [];

        for (var z = TileZoom(geoZoom, viewZoom); z >= 0; z--)
        {
            var factor = Math.Pow(2, z - geoZoom);
            var (ox, oy) = Project(originLat, originLon, geoZoom);
            var tiles = 1 << z;
            var x0 = (int)Math.Floor((ox + left) * factor / TileSize);
            var x1 = (int)Math.Floor((ox + right) * factor / TileSize);
            var y0 = Math.Max(0, (int)Math.Floor((oy + top) * factor / TileSize));
            var y1 = Math.Min(tiles - 1, (int)Math.Floor((oy + bottom) * factor / TileSize));
            if (y1 < y0)
                return [];
            if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) > MaxTiles)
                continue;

            var size = TileSize / factor;
            var result = new List<TilePlacement>();
            for (var ty = y0; ty <= y1; ty++)
                for (var tx = x0; tx <= x1; tx++)
                    result.Add(new TilePlacement(z, ((tx % tiles) + tiles) % tiles, ty, tx * size - ox, ty * size - oy, size));
            return result;
        }
        return [];
    }

    /// <summary>URL del tassello dal modello con {z} {x} {y}.</summary>
    public static string TileUrl(string template, TilePlacement t) => template
        .Replace("{z}", t.Z.ToString(CultureInfo.InvariantCulture))
        .Replace("{x}", t.X.ToString(CultureInfo.InvariantCulture))
        .Replace("{y}", t.Y.ToString(CultureInfo.InvariantCulture));

    /// <summary>Errore del modello di URL dei tasselli (vuoto = OpenStreetMap): http(s), con {z} {x} {y}, senza spazi né virgolette.</summary>
    public static string? ValidateUrl(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
            return null;
        if (!Uri.TryCreate(template.Replace("{z}", "0").Replace("{x}", "0").Replace("{y}", "0"), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http"))
            return "L'indirizzo dei tasselli deve essere un URL http o https.";
        if (!template.Contains("{z}") || !template.Contains("{x}") || !template.Contains("{y}"))
            return "L'indirizzo dei tasselli deve contenere {z}, {x} e {y}.";
        if (template.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'' or '<' or '>'))
            return "L'indirizzo dei tasselli non può contenere spazi, virgolette o < >.";
        return null;
    }

    /// <summary>Legge "44.494, 11.342" (anche con spazi o ";", decimali col punto) come latitudine e longitudine.</summary>
    public static bool TryParseLatLon(string? text, out double latitude, out double longitude)
    {
        latitude = longitude = 0;
        var parts = (text ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out latitude)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out longitude)
            && Math.Abs(latitude) <= MaxLatitude && Math.Abs(longitude) <= 180;
    }

    public static string FormatLatLon(double latitude, double longitude) =>
        string.Create(CultureInfo.InvariantCulture, $"{latitude:0.#####}, {longitude:0.#####}");
}
