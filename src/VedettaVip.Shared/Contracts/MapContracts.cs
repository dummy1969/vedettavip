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
    IReadOnlyList<MapLinkDto> Links);

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
    string? OwnIcon = null);

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

/// <summary>Sposta un nodo su un'altra mappa; i link del nodo sulla mappa di origine vengono eliminati.</summary>
public sealed record MoveMapNodeDto(
    [property: Required] Guid? TargetMapId,
    [property: Range(-1_000_000d, 1_000_000d)] double X,
    [property: Range(-1_000_000d, 1_000_000d)] double Y);

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
