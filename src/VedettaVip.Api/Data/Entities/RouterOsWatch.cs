// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

namespace VedettaVip.Api.Data.Entities;

public enum RouterOsWatchKind { Interface, WireGuardPeer }

public enum RouterOsWatchState { Unknown, Up, Down }

/// <summary>
/// Interfaccia o peer WireGuard di un router scelto per la sorveglianza: se va giù si apre un avviso (evento
/// ThresholdRaised con AlertKey "if:…" / "wg:…"), notificato come le soglie. Key = nome dell'interfaccia o chiave
/// pubblica del peer; Label = come mostrarlo (nome, commento, endpoint).
/// </summary>
public sealed class RouterOsWatch
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }
    public RouterOsWatchKind Kind { get; set; }
    public required string Key { get; set; }
    public required string Label { get; set; }
    public RouterOsWatchState State { get; set; }
    /// <summary>Inizio dello stato attuale (null finché non c'è una lettura).</summary>
    public DateTimeOffset? Since { get; set; }
    /// <summary>Letture giù consecutive: l'avviso si apre alla seconda.</summary>
    public int DownReads { get; set; }
    /// <summary>Motivo dell'ultima lettura (es. "non running", "ultimo handshake 12 min fa").</summary>
    public string? Detail { get; set; }
}
