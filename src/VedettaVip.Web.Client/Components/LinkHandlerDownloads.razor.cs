// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using VedettaVip.Shared.Contracts;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Components;

/// <summary>
/// Download degli installer del gestore dei link (winbox://, vedettavip-ssh://, vedettavip-telnet://) per Windows e Linux, con
/// le istruzioni. I percorsi di WinBox e PuTTY delle Impostazioni sono già scritti nell'installer: l'operatore non li indica.
/// </summary>
public partial class LinkHandlerDownloads
{
    [Inject] private VedettaVipApiClient Api { get; set; } = default!;

    /// <summary>Percorsi impostati (null = non ancora caricati: le istruzioni restano generiche).</summary>
    [Parameter] public RemoteAccessSettingsDto? Settings { get; set; }

    private static string PathText(string? path, string program, string file) => path is { Length: > 0 }
        ? $"{program}: l'installer usa {path}; se lì non c'è, cerca nelle posizioni comuni e infine chiede dove si trova {file}."
        : $"{program}: l'installer cerca nelle posizioni comuni; se non lo trova chiede dove si trova {file}.";
}
