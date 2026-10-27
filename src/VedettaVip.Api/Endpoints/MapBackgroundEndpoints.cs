// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using VedettaVip.Api.Data;
using VedettaVip.Api.Data.Entities;
using VedettaVip.Api.Security;
using VedettaVip.Api.Services;
using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;

namespace VedettaVip.Api.Endpoints;

/// <summary>
/// Sfondo delle mappe: impostazioni (nessuno, immagine, mappa geografica), immagine caricata (nel database) e tasselli delle
/// mappe geografiche (Impostazioni → Mappe). Ogni modifica avvisa i browser con MapsChanged.
/// </summary>
public static class MapBackgroundEndpoints
{
    public static IEndpointRouteBuilder MapMapBackgroundEndpoints(this IEndpointRouteBuilder app)
    {
        var maps = app.MapGroup("/api/maps/{id:guid}/background").WithTags("Map background");
        maps.MapPut("/", UpdateSettingsAsync).RequireOperator();
        maps.MapGet("/image", GetImageAsync);
        maps.MapPut("/image", UploadImageAsync).RequireOperator();
        maps.MapDelete("/image", DeleteImageAsync).RequireOperator();

        var settings = app.MapGroup("/api/settings/maps").WithTags("Settings");
        settings.MapGet("/", GetMapSettingsAsync);
        settings.MapPut("/", UpdateMapSettingsAsync).RequireAdmin();
        return app;
    }

    /// <summary>Sfondo di una mappa per il MapDto (senza i byte dell'immagine).</summary>
    internal static async Task<MapBackgroundDto> LoadAsync(VedettaVipDbContext db, Map map, CancellationToken ct)
    {
        var image = await db.MapBackgrounds.AsNoTracking()
            .Where(i => i.MapId == map.Id)
            .Select(i => new { i.Width, i.Height, i.UpdatedAt })
            .FirstOrDefaultAsync(ct);
        return ToDto(map, image is null ? null : new MapImageDto(image.Width, image.Height, image.UpdatedAt.ToUnixTimeMilliseconds()));
    }

    internal static MapBackgroundDto ToDto(Map map, MapImageDto? image) => new(
        map.BackgroundMode, map.BackgroundOpacityPct, map.BackgroundScalePct, map.BackgroundShowGrid, image,
        map is { GeoLatitude: { } lat, GeoLongitude: { } lon, GeoZoom: { } zoom } ? new MapGeoDto(lat, lon, zoom) : null);

    /// <summary>
    /// Modalità, opacità, scala e griglia; per la mappa geografica origine e livello (obbligatori). Le impostazioni
    /// geografiche restano anche passando a un'altra modalità, così si torna alla cartina senza reinserirle.
    /// </summary>
    private static async Task<Results<Ok<MapBackgroundDto>, NotFound, ValidationProblem, ProblemHttpResult>> UpdateSettingsAsync(
        Guid id, MapBackgroundSettingsDto dto, VedettaVipDbContext db, MapNotifier notifier, CancellationToken ct)
    {
        var map = await db.Maps.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (map is null)
            return TypedResults.NotFound();

        var mode = dto.Mode ?? MapBackgroundMode.None;
        if (mode == MapBackgroundMode.Geo && dto.Geo is null && map.GeoZoom is null)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["geo"] = ["Per la mappa geografica servono centro e livello."]
            });

        map.BackgroundMode = mode;
        map.BackgroundOpacityPct = dto.OpacityPct;
        map.BackgroundScalePct = dto.ScalePct;
        map.BackgroundShowGrid = dto.ShowGrid;
        if (dto.Geo is { } geo)
            (map.GeoLatitude, map.GeoLongitude, map.GeoZoom) = (geo.Latitude, geo.Longitude, geo.Zoom);

        if (await db.TrySaveChangesAsync(ct) is { } saveProblem)
            return saveProblem;

        await notifier.MapsChangedAsync(db, [id]);
        return TypedResults.Ok(await LoadAsync(db, map, ct));
    }

    /// <summary>Immagine con cache lunga quando l'URL porta la versione (cambia a ogni caricamento).</summary>
    private static async Task<Results<FileContentHttpResult, NotFound>> GetImageAsync(
        Guid id, HttpContext http, VedettaVipDbContext db, CancellationToken ct)
    {
        var image = await db.MapBackgrounds.AsNoTracking().FirstOrDefaultAsync(i => i.MapId == id, ct);
        if (image is null)
            return TypedResults.NotFound();

        http.Response.Headers[HeaderNames.CacheControl] = http.Request.Query.ContainsKey("v")
            ? "private, max-age=31536000, immutable"
            : "private, no-cache";
        return TypedResults.File(image.Data, image.ContentType, lastModified: image.UpdatedAt);
    }

    /// <summary>
    /// Carica o sostituisce l'immagine: corpo della richiesta = byte del file (PNG, JPEG o WebP, massimo 5 MB, riconosciuti
    /// dal contenuto). Se la mappa non ha sfondo passa a "immagine".
    /// </summary>
    private static async Task<Results<Ok<MapBackgroundDto>, NotFound, ValidationProblem, ProblemHttpResult>> UploadImageAsync(
        Guid id, HttpRequest request, VedettaVipDbContext db, MapNotifier notifier, TimeProvider time, CancellationToken ct)
    {
        var map = await db.Maps.Include(m => m.Background).FirstOrDefaultAsync(m => m.Id == id, ct);
        if (map is null)
            return TypedResults.NotFound();

        static ValidationProblem Invalid(string message) =>
            TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["image"] = [message] });
        if (request.ContentLength > MapBackground.MaxBytes)
            return Invalid("L'immagine supera i 5 MB.");

        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MapBackground.MaxBytes)
                return Invalid("L'immagine supera i 5 MB.");
            buffer.Write(chunk, 0, read);
        }

        var data = buffer.ToArray();
        if (!ImageInfo.TryRead(data, out var contentType, out var width, out var height))
            return Invalid($"Formato non riconosciuto: servono PNG, JPEG o WebP fino a {ImageInfo.MaxSide} px per lato (niente SVG).");

        var now = time.GetUtcNow();
        if (map.Background is { } existing)
            (existing.ContentType, existing.Data, existing.Width, existing.Height, existing.UpdatedAt) = (contentType, data, width, height, now);
        else
            db.MapBackgrounds.Add(new MapBackground
            {
                MapId = id, ContentType = contentType, Data = data, Width = width, Height = height, UpdatedAt = now
            });
        if (map.BackgroundMode == MapBackgroundMode.None)
            map.BackgroundMode = MapBackgroundMode.Image;

        if (await db.TrySaveChangesAsync(ct) is { } saveProblem)
            return saveProblem;

        await notifier.MapsChangedAsync(db, [id]);
        return TypedResults.Ok(ToDto(map, new MapImageDto(width, height, now.ToUnixTimeMilliseconds())));
    }

    /// <summary>Rimuove l'immagine; se era lo sfondo in uso la mappa torna senza sfondo.</summary>
    private static async Task<Results<Ok<MapBackgroundDto>, NotFound, ProblemHttpResult>> DeleteImageAsync(
        Guid id, VedettaVipDbContext db, MapNotifier notifier, CancellationToken ct)
    {
        var map = await db.Maps.Include(m => m.Background).FirstOrDefaultAsync(m => m.Id == id, ct);
        if (map is null)
            return TypedResults.NotFound();

        if (map.Background is { } image)
            db.MapBackgrounds.Remove(image);
        if (map.BackgroundMode == MapBackgroundMode.Image)
            map.BackgroundMode = MapBackgroundMode.None;

        if (await db.TrySaveChangesAsync(ct) is { } saveProblem)
            return saveProblem;

        await notifier.MapsChangedAsync(db, [id]);
        return TypedResults.Ok(ToDto(map, null));
    }

    internal static async Task<MapSettingsDto> LoadMapSettingsAsync(VedettaVipDbContext db, CancellationToken ct) =>
        await db.MonitoringSettings.AsNoTracking()
            .Where(s => s.Id == MonitoringSettings.SingletonId)
            .Select(s => new MapSettingsDto(s.MapTileUrl, s.MapTileAttribution, s.MapTileAttributionUrl))
            .FirstOrDefaultAsync(ct) ?? MapSettingsDto.Default;

    private static async Task<Ok<MapSettingsDto>> GetMapSettingsAsync(VedettaVipDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await LoadMapSettingsAsync(db, ct));

    /// <summary>Campi vuoti = OpenStreetMap. Cambiare i tasselli ridisegna tutte le mappe aperte.</summary>
    private static async Task<Results<Ok<MapSettingsDto>, ValidationProblem, ProblemHttpResult>> UpdateMapSettingsAsync(
        MapSettingsDto dto, VedettaVipDbContext db, MapNotifier notifier, CancellationToken ct)
    {
        static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        var clean = new MapSettingsDto(Blank(dto.TileUrl), Blank(dto.Attribution), Blank(dto.AttributionUrl));

        var errors = new Dictionary<string, string[]>();
        if (GeoTiles.ValidateUrl(clean.TileUrl) is { } urlError)
            errors[nameof(dto.TileUrl)] = [urlError];
        if (clean.AttributionUrl is { } link
            && (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")))
            errors[nameof(dto.AttributionUrl)] = ["Il link dell'attribuzione deve essere un URL http o https."];
        if (clean.TileUrl is not null && clean.Attribution is null)
            errors[nameof(dto.Attribution)] = ["Con tasselli diversi da OpenStreetMap indicare l'attribuzione richiesta dal fornitore."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var settings = await db.MonitoringSettings.FirstAsync(s => s.Id == MonitoringSettings.SingletonId, ct);
        (settings.MapTileUrl, settings.MapTileAttribution, settings.MapTileAttributionUrl) = (clean.TileUrl, clean.Attribution, clean.AttributionUrl);

        if (await db.TrySaveChangesAsync(ct) is { } saveProblem)
            return saveProblem;

        await notifier.AllMapsChangedAsync();
        return TypedResults.Ok(clean);
    }
}
