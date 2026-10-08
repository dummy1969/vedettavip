// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using VedettaVip.Shared.Contracts;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Components;

/// <summary>Impostazioni → WinBox: percorsi di WinBox 4 scritti negli installer del gestore dei link winbox://.</summary>
public partial class WinBoxSettingsCard
{
    [Inject] private VedettaVipApiClient Api { get; set; } = default!;

    private bool loaded;
    private bool busy;
    private string? error;
    private DateTime? savedAt;
    private string? windowsPath;
    private string? linuxPath;
    /// <summary>Valori salvati: quelli che finiscono negli installer scaricati.</summary>
    private WinBoxSettingsDto? saved;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            saved = await Api.GetWinBoxSettingsAsync(CancellationToken.None);
            (windowsPath, linuxPath) = (saved.WindowsPath, saved.LinuxPath);
            loaded = true;
        }
        catch (HttpRequestException ex)
        {
            error = $"Impostazioni di WinBox non caricate: {ex.Message}";
        }
    }

    private async Task SaveAsync()
    {
        busy = true;
        error = null;
        savedAt = null;
        try
        {
            saved = await Api.UpdateWinBoxSettingsAsync(new WinBoxSettingsDto(windowsPath, linuxPath), CancellationToken.None);
            (windowsPath, linuxPath) = (saved.WindowsPath, saved.LinuxPath);
            savedAt = DateTime.Now;
        }
        catch (HttpRequestException ex)
        {
            error = ex.Message;
        }
        finally
        {
            busy = false;
        }
    }
}
