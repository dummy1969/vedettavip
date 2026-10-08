// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using VedettaVip.Shared.Contracts;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Components;

/// <summary>
/// Download degli installer del gestore dei link winbox:// (Windows e Linux), con le istruzioni. Il percorso di WinBox
/// delle Impostazioni è già scritto nell'installer: l'operatore non deve indicarlo.
/// </summary>
public partial class WinBoxHandlerDownloads
{
    [Inject] private VedettaVipApiClient Api { get; set; } = default!;

    /// <summary>Percorsi impostati (null = non ancora caricati: le istruzioni restano generiche).</summary>
    [Parameter] public WinBoxSettingsDto? Settings { get; set; }

    private static string PathText(string? path, string file) => path is { Length: > 0 }
        ? $"L'installer usa {path}; se lì non c'è, cerca WinBox nelle posizioni comuni e infine chiede dove si trova {file}."
        : $"L'installer cerca WinBox nelle posizioni comuni; se non lo trova chiede dove si trova {file}.";
}
