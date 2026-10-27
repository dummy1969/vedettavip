// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.ComponentModel.DataAnnotations;
using VedettaVip.Shared.Models;

namespace VedettaVip.Shared.Contracts;

public sealed record MapSummaryDto(Guid Id, string Name, Guid? ParentMapId, int NodeCount);

public sealed record MapRefDto(Guid Id, string Name);

public sealed record MapDto(
    Guid Id,
    string Name,
    Guid? ParentMapId,
    string? BackgroundImage,
    int GridSize,
    IReadOnlyList<MapNodeDto> Nodes,
    IReadOnlyList<MapLinkDto> Links,
    MapBackgroundDto? Background = null);

/// <summary>
/// Sfondo della mappa. Image: dimensioni e versione dell'immagine caricata (null = non ancora caricata), disegnata da (0, 0)
/// alla scala indicata. Geo: origine e livello della mappa geografica (vedi <see cref="GeoTiles"/>).
/// </summary>
public sealed record MapBackgroundDto(
    MapBackgroundMode Mode,
    int OpacityPct,
    int ScalePct,
    bool ShowGrid,
    MapImageDto? Image,
    MapGeoDto? Geo)
{
    public static readonly MapBackgroundDto None = new(MapBackgroundMode.None, 100, 100, false, null, null);
}

public sealed record MapImageDto(int Width, int Height, long Version);

/// <summary>Mappa geografica: latitudine e longitudine del punto (0, 0) della mappa e livello di riferimento dei tasselli.</summary>
public sealed record MapGeoDto(
    [property: Range(-GeoTiles.MaxLatitude, GeoTiles.MaxLatitude, ErrorMessage = "Latitudine non valida.")] double Latitude,
    [property: Range(-180d, 180d, ErrorMessage = "Longitudine non valida.")] double Longitude,
    [property: Range(GeoTiles.MinZoom, GeoTiles.MaxZoom, ErrorMessage = "Livello non valido.")] int Zoom);

/// <summary>Impostazioni dello sfondo (PUT /api/maps/{id}/background): l'immagine si carica a parte.</summary>
public sealed record MapBackgroundSettingsDto(
    [property: Required] MapBackgroundMode? Mode,
    [property: Range(10, 100, ErrorMessage = "L'opacità va da 10 a 100.")] int OpacityPct = 100,
    [property: Range(1, 1000, ErrorMessage = "La scala va da 1 a 1000.")] int ScalePct = 100,
    bool ShowGrid = false,
    MapGeoDto? Geo = null);

/// <summary>
/// Tasselli delle mappe geografiche (Impostazioni → Mappe): modello di URL con {z} {x} {y} e attribuzione; null o vuoto =
/// OpenStreetMap.
/// </summary>
public sealed record MapSettingsDto(
    [property: StringLength(MapSettingsDto.MaxUrlLength)] string? TileUrl,
    [property: StringLength(MapSettingsDto.MaxAttributionLength)] string? Attribution,
    [property: StringLength(MapSettingsDto.MaxUrlLength)] string? AttributionUrl)
{
    public const int MaxUrlLength = 512, MaxAttributionLength = 256;

    public static readonly MapSettingsDto Default = new(null, null, null);

    public string EffectiveTileUrl => string.IsNullOrWhiteSpace(TileUrl) ? GeoTiles.DefaultUrl : TileUrl;
    public string EffectiveAttribution => string.IsNullOrWhiteSpace(Attribution) ? GeoTiles.DefaultAttribution : Attribution;
    /// <summary>Link dell'attribuzione: quello impostato, o la pagina dei diritti di OSM se si usano i tasselli predefiniti.</summary>
    public string? EffectiveAttributionUrl => !string.IsNullOrWhiteSpace(AttributionUrl) ? AttributionUrl
        : string.IsNullOrWhiteSpace(TileUrl) ? GeoTiles.DefaultAttributionUrl : null;
}

/// <summary>
/// Nodo pronto per la mappa: Name/Address/DeviceType arrivano dal dispositivo (o dalla sottomappa),
/// State è calcolato lato server (Unknown finché non c'è il poller).
/// </summary>
public sealed record MapNodeDto(
    Guid Id,
    MapNodeKind Kind,
    Guid? DeviceId,
    Guid? SubmapId,
    double X,
    double Y,
    string LabelTemplate,
    string? Icon,
    string? Name,
    string? Address,
    DeviceType? DeviceType,
    NodeState State,
    /// <summary>Nome della finestra di manutenzione attiva che copre il device del nodo (null = nessuna).</summary>
    string? Maintenance = null,
    /// <summary>
    /// Solo nodi Submap: dispositivi della sottomappa e delle sue discendenti (una volta sola ciascuno), con lo stato.
    /// Il client ricalcola lo stato aggregato a ogni DeviceStateChanged senza ricaricare la mappa.
    /// </summary>
    IReadOnlyList<SubmapMemberDto>? SubmapMembers = null,
    /// <summary>Icona propria del nodo (null = quella del dispositivo o la predefinita); <c>Icon</c> è già quella effettiva.</summary>
    string? OwnIcon = null,
    /// <summary>Produttore del dispositivo del nodo (null per sottomappe e nodi statici): decide le voci del menu contestuale.</summary>
    DeviceVendor? Vendor = null,
    /// <summary>Accessi remoti effettivi del dispositivo (null per sottomappe e nodi statici).</summary>
    DeviceAccessDto? Access = null);

public sealed record SubmapMemberDto(Guid DeviceId, NodeState State, bool InMaintenance);

public sealed record MapLinkDto(
    Guid Id,
    Guid FromNodeId,
    Guid ToNodeId,
    Guid? DeviceId,
    int? IfIndex,
    long SpeedBps,
    int? UtilizationThresholdPct = null);

public sealed record NodePositionDto(
    [property: Range(-1_000_000d, 1_000_000d)] double X,
    [property: Range(-1_000_000d, 1_000_000d)] double Y);

/// <summary>Nuova posizione di un nodo in un salvataggio in blocco (es. "Metti in colonna le sottomappe").</summary>
public sealed record NodePositionItemDto(
    Guid NodeId,
    [property: Range(-1_000_000d, 1_000_000d)] double X,
    [property: Range(-1_000_000d, 1_000_000d)] double Y);

/// <summary>Posizioni di più nodi della stessa mappa, salvate insieme (tutte o nessuna).</summary>
public sealed record NodePositionsDto(
    [property: Required, MinLength(1), MaxLength(NodePositionsDto.MaxNodes)] IReadOnlyList<NodePositionItemDto> Nodes)
{
    public const int MaxNodes = 1000;
}

public sealed record CreateMapNodeDto(
    [property: Required] MapNodeKind? Kind,
    Guid? DeviceId,
    Guid? SubmapId,
    [property: Range(-1_000_000d, 1_000_000d)] double X,
    [property: Range(-1_000_000d, 1_000_000d)] double Y,
    [property: StringLength(512)] string? LabelTemplate,
    [property: StringLength(64)] string? Icon);

/// <summary>Etichetta e icona di un nodo; LabelTemplate vuoto = etichetta di default (obbligatorio per i nodi Static).</summary>
public sealed record UpdateMapNodeDto(
    [property: StringLength(512, ErrorMessage = "L'etichetta supera i 512 caratteri.")] string? LabelTemplate,
    [property: StringLength(64)] string? Icon = null);

/// <summary>
/// Sposta un nodo su un'altra mappa; i link del nodo sulla mappa di origine vengono eliminati. Un nodo Submap porta con
/// sé la sottomappa con tutto il suo contenuto. Senza X/Y il nodo va sotto i nodi esistenti della mappa di destinazione.
/// </summary>
public sealed record MoveMapNodeDto(
    [property: Required] Guid? TargetMapId,
    [property: Range(-1_000_000d, 1_000_000d)] double? X = null,
    [property: Range(-1_000_000d, 1_000_000d)] double? Y = null);

/// <summary>
/// Creazione o modifica di una mappa. ParentMapId non è modificabile qui: lo mantiene l'API
/// quando si crea, sposta o elimina il nodo Submap che rappresenta la mappa.
/// </summary>
public sealed record MapUpsertDto(
    [property: Required(ErrorMessage = "Il nome è obbligatorio."), StringLength(128, ErrorMessage = "Il nome supera i 128 caratteri.")] string Name,
    [property: StringLength(512)] string? BackgroundImage = null,
    [property: Range(1, 500, ErrorMessage = "GridSize deve essere tra 1 e 500.")] int GridSize = 20);

/// <summary>
/// Creazione o modifica di un link. I due nodi devono stare sulla mappa del link; DeviceId + IfIndex
/// indicano l'interfaccia da cui leggere il traffico (tx = From → To).
/// </summary>
public sealed record MapLinkUpsertDto(
    [property: Required(ErrorMessage = "Il nodo di partenza è obbligatorio.")] Guid? FromNodeId,
    [property: Required(ErrorMessage = "Il nodo di arrivo è obbligatorio.")] Guid? ToNodeId,
    Guid? DeviceId = null,
    [property: Range(1, int.MaxValue, ErrorMessage = "IfIndex deve essere maggiore di 0.")] int? IfIndex = null,
    [property: Range(1L, long.MaxValue, ErrorMessage = "SpeedBps deve essere maggiore di 0.")] long SpeedBps = 1_000_000_000,
    /// <summary>Soglia di utilizzo in %: null = generale, 0 = disattivata.</summary>
    [property: Range(0, 100, ErrorMessage = "Soglia di utilizzo: tra 0 (disattivata) e 100 %.")] int? UtilizationThresholdPct = null);

/// <summary>
/// Messaggio SignalR "MapsChanged" sull'hub /hubs/status: mappe modificate (nodi, link, nome, device mostrati, manutenzione).
/// I browser che le hanno aperte le ricaricano. MapIds null = tutte le mappe.
/// </summary>
/// <param name="ClientId">Header <see cref="MapClientHeaders.ClientId"/> della richiesta che ha fatto la modifica:
/// la scheda del browser che l'ha fatta la riconosce e non ricarica (ha già il risultato).</param>
public sealed record MapsChangedDto(IReadOnlyList<Guid>? MapIds, string? ClientId);

public static class MapClientHeaders
{
    /// <summary>Id casuale della scheda del browser, inviato con ogni chiamata all'API e riportato in <see cref="MapsChangedDto"/>.</summary>
    public const string ClientId = "X-VedettaVip-Client";
}
