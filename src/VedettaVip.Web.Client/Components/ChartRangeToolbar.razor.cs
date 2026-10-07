// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;

namespace VedettaVip.Web.Client.Components;

/// <summary>Barra dei periodi dei grafici storici, con informazioni sulla serie (es. risoluzione).</summary>
public partial class ChartRangeToolbar
{
    [Parameter, EditorRequired] public IReadOnlyList<(string Label, TimeSpan Range)> Ranges { get; set; } = [];
    [Parameter] public TimeSpan Selected { get; set; }
    [Parameter] public EventCallback<TimeSpan> OnSelect { get; set; }
    [Parameter] public string? Info { get; set; }
    [Parameter] public bool Loading { get; set; }
}
