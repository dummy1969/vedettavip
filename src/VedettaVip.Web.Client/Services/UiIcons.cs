// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

namespace VedettaVip.Web.Client.Services;

/// <summary>
/// Icone dei comandi dell'interfaccia (Tabler, generate da third-party/tabler-icons/generate.py in UiIcons.g.cs):
/// separate da <see cref="MapIcons"/>, così non compaiono nel selettore delle icone dei nodi.
/// </summary>
public static partial class UiIcons
{
    public const string Charts = "chart-line", WinBox = "app-window", Edit = "pencil", Delete = "trash";

    // Pigro: All sta nell'altro file partial e l'ordine di inizializzazione dei campi statici tra file non è garantito
    private static Dictionary<string, MapIcon>? byKey;
    private static Dictionary<string, MapIcon> ByKey => byKey ??= All.ToDictionary(i => i.Key);

    public static MapIcon? Find(string? key) => key is not null && ByKey.TryGetValue(key, out var icon) ? icon : null;
}
