// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

namespace VedettaVip.Api.Data.Entities;

/// <summary>
/// Avviso di soglia attualmente aperto (stato del valutatore, lo storico è negli eventi ThresholdRaised/Cleared).
/// Chiave: tipo + device + link (Guid.Empty per latenza e perdita del device).
/// </summary>
public sealed class OpenThresholdAlert
{
    public required string Kind { get; set; }
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }
    public Guid LinkId { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public double Threshold { get; set; }
    public double LastValue { get; set; }
    public double PeakValue { get; set; }
}
