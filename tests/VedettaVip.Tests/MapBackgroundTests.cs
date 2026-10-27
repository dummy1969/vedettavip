// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Text;
using VedettaVip.Api.Services;
using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;

namespace VedettaVip.Tests;

/// <summary>Sfondo delle mappe: riconoscimento delle immagini dal contenuto e calcoli della mappa geografica.</summary>
public class MapBackgroundTests
{
    // ---------- Immagini ----------

    private static byte[] Png(uint width, uint height)
    {
        byte[] data = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, .. "IHDR"u8, 0, 0, 0, 0, 0, 0, 0, 0, 8, 6, 0, 0, 0];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20), height);
        return data;
    }

    /// <summary>SOI, APP0 (JFIF, 16 byte), SOF0 con altezza e larghezza.</summary>
    private static byte[] Jpeg(ushort width, ushort height, byte sof = 0xC0) =>
    [
        0xFF, 0xD8,
        0xFF, 0xE0, 0x00, 0x10, .. "JFIF\0"u8, 1, 1, 0, 0, 1, 0, 1, 0, 0,
        0xFF, sof, 0x00, 0x11, 8, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1
    ];

    private static byte[] WebP(string chunk, byte[] body) =>
        [.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8, .. Encoding.ASCII.GetBytes(chunk), (byte)body.Length, 0, 0, 0, .. body, .. new byte[16]];

    [Fact]
    public void Png_is_recognized_with_its_size()
    {
        Assert.True(ImageInfo.TryRead(Png(1920, 1080), out var type, out var w, out var h));
        Assert.Equal(("image/png", 1920, 1080), (type, w, h));
    }

    [Fact]
    public void Jpeg_size_comes_from_the_first_sof_after_other_segments()
    {
        Assert.True(ImageInfo.TryRead(Jpeg(1600, 900), out var type, out var w, out var h));
        Assert.Equal(("image/jpeg", 1600, 900), (type, w, h));
        Assert.True(ImageInfo.TryRead(Jpeg(800, 600, sof: 0xC2), out _, out w, out h)); // progressivo
        Assert.Equal((800, 600), (w, h));
        Assert.False(ImageInfo.TryRead(Jpeg(800, 600, sof: 0xC4), out _, out _, out _)); // DHT non è un SOF, poi il file finisce
    }

    [Fact]
    public void WebP_lossy_lossless_and_extended_are_recognized()
    {
        // VP8: intestazione del frame (3 byte), firma 9D 01 2A, larghezza e altezza a 14 bit
        Assert.True(ImageInfo.TryRead(WebP("VP8 ", [0, 0, 0, 0x9D, 0x01, 0x2A, 0x80, 0x02, 0xE0, 0x01]), out var type, out var w, out var h));
        Assert.Equal(("image/webp", 640, 480), (type, w, h));

        // VP8L: firma 2F, poi (larghezza - 1) e (altezza - 1) a 14 bit
        uint bits = (1024 - 1) | ((768u - 1) << 14);
        Assert.True(ImageInfo.TryRead(WebP("VP8L", [0x2F, (byte)bits, (byte)(bits >> 8), (byte)(bits >> 16), (byte)(bits >> 24)]), out _, out w, out h));
        Assert.Equal((1024, 768), (w, h));

        // VP8X: 4 byte di flag, poi (larghezza - 1) e (altezza - 1) a 24 bit
        Assert.True(ImageInfo.TryRead(WebP("VP8X", [0, 0, 0, 0, 0x7F, 0x07, 0, 0x37, 0x04, 0]), out _, out w, out h));
        Assert.Equal((1920, 1080), (w, h));
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    [InlineData("GIF89a..........")]
    [InlineData("")]
    [InlineData("not an image at all, just text")]
    public void Anything_else_is_rejected(string content) =>
        Assert.False(ImageInfo.TryRead(Encoding.UTF8.GetBytes(content), out _, out _, out _));

    [Fact]
    public void Truncated_empty_or_huge_images_are_rejected()
    {
        Assert.False(ImageInfo.TryRead(Png(1920, 1080)[..20], out _, out _, out _));
        Assert.False(ImageInfo.TryRead(Png(0, 100), out _, out _, out _));
        Assert.False(ImageInfo.TryRead(Png(ImageInfo.MaxSide + 1, 100), out _, out _, out _));
        Assert.False(ImageInfo.TryRead(Jpeg(1600, 900)[..12], out _, out _, out _));
    }

    // ---------- Mappa geografica ----------

    [Fact]
    public void Projection_matches_the_tile_grid()
    {
        Assert.Equal((256d, 256d), GeoTiles.Project(0, 0, 1));          // centro del mondo a z1 (512 px)
        var (x, y) = GeoTiles.Project(GeoTiles.MaxLatitude, -180, 3);
        Assert.Equal(0, x, 6);
        Assert.Equal(0, y, 0);                                           // angolo in alto a sinistra

        // Bologna (44.4949, 11.3426) a z12 cade nel tassello 2177/1481 (formula standard dei tasselli OSM, verificata a parte)
        var (bx, by) = GeoTiles.Project(44.4949, 11.3426, 12);
        Assert.Equal((2177, 1481), ((int)(bx / 256), (int)(by / 256)));
    }

    [Theory]
    [InlineData(44.4949, 11.3426, 12)]
    [InlineData(-33.8688, 151.2093, 15)]
    [InlineData(0, 0, 1)]
    public void Unproject_inverts_project(double lat, double lon, int zoom)
    {
        var (x, y) = GeoTiles.Project(lat, lon, zoom);
        var (lat2, lon2) = GeoTiles.Unproject(x, y, zoom);
        Assert.Equal(lat, lat2, 9);
        Assert.Equal(lon, lon2, 9);
    }

    [Fact]
    public void Origin_puts_the_chosen_map_point_on_the_center()
    {
        // I nodi esistenti hanno il centro in (500, 300): dopo la scelta di Bologna quel punto è Bologna
        var (olat, olon) = GeoTiles.OriginFor(44.4949, 11.3426, 12, 500, 300);
        var (lat, lon) = GeoTiles.ToLatLon(olat, olon, 12, 500, 300);
        Assert.Equal(44.4949, lat, 9);
        Assert.Equal(11.3426, lon, 9);
    }

    [Theory]
    [InlineData(1.0, 12)]
    [InlineData(2.0, 13)]
    [InlineData(0.5, 11)]
    [InlineData(0.25, 10)]
    [InlineData(1.3, 12)]
    [InlineData(1000.0, GeoTiles.MaxZoom)]
    public void Tile_level_follows_the_view_zoom(double viewZoom, int expected) =>
        Assert.Equal(expected, GeoTiles.TileZoom(12, viewZoom));

    [Fact]
    public void Visible_tiles_cover_the_view_and_line_up_with_the_map()
    {
        var (olat, olon) = GeoTiles.OriginFor(44.4949, 11.3426, 12, 0, 0);
        var tiles = GeoTiles.Visible(olat, olon, 12, 0, 0, 1000, 600, 1.0);

        Assert.All(tiles, t => Assert.Equal(12, t.Z));
        Assert.All(tiles, t => Assert.Equal(256d, t.Size));
        // Il tassello che contiene il punto (0, 0) è quello di Bologna, e lo copre davvero
        var home = Assert.Single(tiles, t => t.MapX <= 0 && t.MapX + t.Size > 0 && t.MapY <= 0 && t.MapY + t.Size > 0);
        Assert.Equal((2177, 1481), (home.X, home.Y));
        // Copertura: 1000 × 600 con tasselli da 256 = 4–5 colonne × 3–4 righe, secondo dove cade l'origine nel tassello
        Assert.InRange(tiles.Count, 12, 20);
        Assert.True(tiles.Min(t => t.MapX) <= 0 && tiles.Max(t => t.MapX + t.Size) >= 1000);
        Assert.True(tiles.Min(t => t.MapY) <= 0 && tiles.Max(t => t.MapY + t.Size) >= 600);
    }

    [Fact]
    public void Zooming_out_uses_bigger_tiles_and_never_too_many()
    {
        var (olat, olon) = GeoTiles.OriginFor(44.4949, 11.3426, 12, 0, 0);
        var half = GeoTiles.Visible(olat, olon, 12, -1000, -600, 1000, 600, 0.5);
        Assert.All(half, t => Assert.Equal((11, 512d), (t.Z, t.Size)));

        // Vista enorme a zoom 1: il livello scende finché i tasselli restano pochi
        var huge = GeoTiles.Visible(olat, olon, 12, -200_000, -200_000, 200_000, 200_000, 1.0);
        Assert.InRange(huge.Count, 1, GeoTiles.MaxTiles);
        Assert.True(huge[0].Z < 12);
    }

    [Fact]
    public void World_wraps_horizontally_but_not_vertically()
    {
        // Origine a longitudine -180 e latitudine massima: a sinistra c'è il mondo ripetuto, sopra niente
        var tiles = GeoTiles.Visible(GeoTiles.MaxLatitude, -180, 2, -300, -300, 300, 300, 1.0);
        Assert.All(tiles, t => Assert.InRange(t.X, 0, 3));
        Assert.All(tiles, t => Assert.True(t.Y >= 0));
        Assert.Contains(tiles, t => t.X == 3 && t.MapX < 0);   // l'ultima colonna del mondo, a sinistra dell'origine
        Assert.Empty(GeoTiles.Visible(GeoTiles.MaxLatitude, -180, 2, 0, -900, 100, -800, 1.0));
    }

    [Fact]
    public void Tile_url_is_filled_in()
    {
        var url = GeoTiles.TileUrl(GeoTiles.DefaultUrl, new TilePlacement(12, 2177, 1495, 0, 0, 256));
        Assert.Equal("https://tile.openstreetmap.org/12/2177/1495.png", url);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("https://tile.openstreetmap.org/{z}/{x}/{y}.png", true)]
    [InlineData("https://api.maptiler.com/maps/streets/{z}/{x}/{y}.png?key=abc", true)]
    [InlineData("http://tiles.example.com/{z}/{x}/{y}.png", true)]
    [InlineData("https://tile.example.com/{z}/{x}.png", false)]
    [InlineData("javascript:alert(1)//{z}{x}{y}", false)]
    [InlineData("ftp://tiles.example.com/{z}/{x}/{y}.png", false)]
    [InlineData("https://tiles.example.com/{z}/{x}/{y}.png\" onload=\"x", false)]
    [InlineData("tiles/{z}/{x}/{y}.png", false)]
    public void Tile_url_template_is_validated(string? template, bool valid) =>
        Assert.Equal(valid, GeoTiles.ValidateUrl(template) is null);

    [Theory]
    [InlineData("44.494, 11.342", true, 44.494, 11.342)]
    [InlineData("44.494,11.342", true, 44.494, 11.342)]
    [InlineData(" -33.8688 ; 151.2093 ", true, -33.8688, 151.2093)]
    [InlineData("44,494, 11,342", false, 0, 0)]
    [InlineData("91, 10", false, 0, 0)]
    [InlineData("44.5", false, 0, 0)]
    [InlineData("Bologna", false, 0, 0)]
    public void Center_is_parsed_from_pasted_coordinates(string text, bool ok, double lat, double lon)
    {
        Assert.Equal(ok, GeoTiles.TryParseLatLon(text, out var la, out var lo));
        if (ok)
            Assert.Equal((lat, lon), (la, lo));
    }

    [Fact]
    public void Default_tiles_are_openstreetmap_with_its_attribution()
    {
        var settings = MapSettingsDto.Default;
        Assert.Equal(GeoTiles.DefaultUrl, settings.EffectiveTileUrl);
        Assert.Equal("© OpenStreetMap contributors", settings.EffectiveAttribution);
        Assert.Equal(GeoTiles.DefaultAttributionUrl, settings.EffectiveAttributionUrl);

        var custom = new MapSettingsDto("https://tiles.example.com/{z}/{x}/{y}.png", "© Example", null);
        Assert.Null(custom.EffectiveAttributionUrl); // il link di OSM non vale per un altro fornitore
    }
}
