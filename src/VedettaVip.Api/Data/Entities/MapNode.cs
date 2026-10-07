// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Data.Entities;

public sealed class MapNode
{
    public Guid Id { get; set; }
    public Guid MapId { get; set; }
    public Map? Map { get; set; }
    public MapNodeKind Kind { get; set; }
    public Guid? DeviceId { get; set; }
    public Device? Device { get; set; }
    public Guid? SubmapId { get; set; }
    public Map? Submap { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public string LabelTemplate { get; set; } = DefaultLabelTemplate;
    public string? Icon { get; set; }

    public const string DefaultLabelTemplate = "[Name]\n[Address]";
}
