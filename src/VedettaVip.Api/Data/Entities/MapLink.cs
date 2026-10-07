// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

namespace VedettaVip.Api.Data.Entities;

public sealed class MapLink
{
    public Guid Id { get; set; }
    public Guid MapId { get; set; }
    public Map? Map { get; set; }
    public Guid FromNodeId { get; set; }
    public MapNode? FromNode { get; set; }
    public Guid ToNodeId { get; set; }
    public MapNode? ToNode { get; set; }
    /// <summary>Dispositivo e interfaccia da cui leggere il traffico (tx = From → To).</summary>
    public Guid? DeviceId { get; set; }
    public Device? Device { get; set; }
    public int? IfIndex { get; set; }
    public long SpeedBps { get; set; } = 1_000_000_000;
    /// <summary>Soglia di utilizzo del link in %; null = generale, 0 = disattivata.</summary>
    public int? UtilizationThresholdPct { get; set; }
}
