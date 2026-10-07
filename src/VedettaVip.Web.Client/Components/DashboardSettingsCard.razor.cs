// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using VedettaVip.Shared.Contracts;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Components;

/// <summary>Impostazioni della dashboard (Home): periodo predefinito degli eventi recenti.</summary>
public partial class DashboardSettingsCard
{
    [Inject] private VedettaVipApiClient Api { get; set; } = default!;

    private bool loaded;
    private bool busy;
    private string? error;
    private DateTime? savedAt;
    private int hours = DashboardSettingsDto.Default.RecentEventHours;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            hours = (await Api.GetDashboardSettingsAsync(CancellationToken.None)).RecentEventHours;
            loaded = true;
        }
        catch (HttpRequestException ex)
        {
            error = $"Impostazioni della dashboard non caricate: {ex.Message}";
        }
    }

    private async Task SaveAsync()
    {
        busy = true;
        error = null;
        savedAt = null;
        try
        {
            await Api.UpdateDashboardSettingsAsync(new DashboardSettingsDto(hours), CancellationToken.None);
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
