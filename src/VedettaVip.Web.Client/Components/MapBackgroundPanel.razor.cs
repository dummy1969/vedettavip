// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;
using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Components;

/// <summary>
/// Pannello "Sfondo" della mappa (modalità Modifica): nessuno, immagine caricata (scala) o mappa geografica (centro e
/// dettaglio), opacità e griglia. Ogni modifica è un'anteprima sulla mappa (<see cref="OnPreview"/>); "Salva" la registra,
/// "Chiudi" la annulla. L'immagine invece si carica e si rimuove subito (è un file, non un'impostazione).
/// </summary>
public partial class MapBackgroundPanel : IDisposable
{
    private const int MaxBytes = 5 * 1024 * 1024;

    [Inject] private VedettaVipApiClient Api { get; set; } = default!;
    [Inject] private ILogger<MapBackgroundPanel> Logger { get; set; } = default!;

    [Parameter, EditorRequired] public Guid MapId { get; set; }
    /// <summary>Sfondo salvato della mappa.</summary>
    [Parameter, EditorRequired] public MapBackgroundDto Background { get; set; } = MapBackgroundDto.None;
    /// <summary>Nodi della mappa: il loro centro va sul centro geografico scelto.</summary>
    [Parameter, EditorRequired] public IReadOnlyList<MapNode> Nodes { get; set; } = [];
    /// <summary>Sfondo da mostrare finché il pannello è aperto (null = quello salvato).</summary>
    [Parameter] public EventCallback<MapBackgroundDto?> OnPreview { get; set; }
    /// <summary>Sfondo salvato (impostazioni o immagine): la pagina aggiorna la mappa.</summary>
    [Parameter] public EventCallback<MapBackgroundDto> OnSaved { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private readonly CancellationTokenSource cts = new();
    private bool initialized;
    private bool busy;
    private string? error;

    // Select legati a stringhe e interi (vedi CLAUDE.md: i select su tipi non stringa non si agganciano bene)
    private string mode = nameof(MapBackgroundMode.None);
    private int opacityPct = 100;
    private int scalePct = 100;
    private bool showGrid;
    private string centerText = "";
    private int geoZoom = 12;
    private MapImageDto? image;

    private MapBackgroundMode Mode => Enum.TryParse<MapBackgroundMode>(mode, out var m) ? m : MapBackgroundMode.None;

    protected override void OnParametersSet()
    {
        if (initialized)
            return;
        initialized = true;
        Load(Background);
    }

    private void Load(MapBackgroundDto bg)
    {
        mode = bg.Mode.ToString();
        opacityPct = bg.OpacityPct;
        scalePct = bg.ScalePct;
        showGrid = bg.ShowGrid;
        image = bg.Image;
        if (bg.Geo is { } geo)
        {
            // Il centro mostrato è il punto geografico sotto il centro dei nodi
            var (cx, cy) = NodesCenter();
            var (lat, lon) = GeoTiles.ToLatLon(geo.Latitude, geo.Longitude, geo.Zoom, cx, cy);
            centerText = GeoTiles.FormatLatLon(lat, lon);
            geoZoom = geo.Zoom;
        }
    }

    /// <summary>Centro del riquadro dei nodi; mappa vuota: un punto vicino all'angolo in alto a sinistra, dove nascono i nodi.</summary>
    private (double X, double Y) NodesCenter() => Nodes.Count == 0
        ? (400, 300)
        : ((Nodes.Min(n => n.X) + Nodes.Max(n => n.X)) / 2, (Nodes.Min(n => n.Y) + Nodes.Max(n => n.Y)) / 2);

    /// <summary>Origine della mappa geografica dal centro scritto; null se non valido.</summary>
    private MapGeoDto? GeoFromForm()
    {
        if (!GeoTiles.TryParseLatLon(centerText, out var lat, out var lon))
            return null;
        var (cx, cy) = NodesCenter();
        var (olat, olon) = GeoTiles.OriginFor(lat, lon, geoZoom, cx, cy);
        return new MapGeoDto(olat, olon, geoZoom);
    }

    private MapBackgroundDto FromForm() => new(Mode, Math.Clamp(opacityPct, 10, 100), Math.Clamp(scalePct, 1, 1000), showGrid,
        image, Mode == MapBackgroundMode.Geo ? GeoFromForm() : Background.Geo);

    private bool CanSave() => Mode != MapBackgroundMode.Geo || GeoFromForm() is not null;

    private Task Preview() => OnPreview.InvokeAsync(FromForm());

    private async Task SaveAsync()
    {
        if (!CanSave()) return;
        var form = FromForm();
        await RunAsync(async ct =>
        {
            var saved = await Api.UpdateMapBackgroundAsync(MapId, new MapBackgroundSettingsDto(form.Mode, form.OpacityPct, form.ScalePct,
                form.ShowGrid, form.Mode == MapBackgroundMode.Geo ? form.Geo : null), ct);
            await OnSaved.InvokeAsync(saved);
            await OnClose.InvokeAsync();
        });
    }

    /// <summary>Carica subito il file scelto (il formato lo verifica l'API dal contenuto).</summary>
    private async Task UploadAsync(InputFileChangeEventArgs e)
    {
        var file = e.File;
        if (file.Size > MaxBytes)
        {
            error = "L'immagine supera i 5 MB.";
            return;
        }

        await RunAsync(async ct =>
        {
            using var buffer = new MemoryStream();
            await using (var stream = file.OpenReadStream(MaxBytes, ct))
                await stream.CopyToAsync(buffer, ct);
            var saved = await Api.UploadMapBackgroundImageAsync(MapId, buffer.ToArray(), file.ContentType, ct);
            image = saved.Image;
            await OnSaved.InvokeAsync(saved);
            await Preview(); // le altre impostazioni del pannello, non ancora salvate, restano in anteprima
        });
    }

    private async Task DeleteImageAsync()
    {
        await RunAsync(async ct =>
        {
            var saved = await Api.DeleteMapBackgroundImageAsync(MapId, ct);
            image = null;
            if (saved.Mode == MapBackgroundMode.None)
                mode = nameof(MapBackgroundMode.None);
            await OnSaved.InvokeAsync(saved);
            await Preview();
        });
    }

    private async Task CloseAsync()
    {
        await OnPreview.InvokeAsync(null);
        await OnClose.InvokeAsync();
    }

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        busy = true;
        error = null;
        try
        {
            await action(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            Logger.LogWarning("Sfondo della mappa non salvato: {Message}", ex.Message);
            error = ex.Message;
        }
        finally
        {
            busy = false;
        }
    }

    public void Dispose()
    {
        cts.Cancel();
        cts.Dispose();
    }
}
