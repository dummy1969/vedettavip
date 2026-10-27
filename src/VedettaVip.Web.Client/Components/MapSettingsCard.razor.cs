// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using VedettaVip.Shared.Contracts;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Components;

/// <summary>Impostazioni delle mappe: fornitore dei tasselli delle mappe geografiche e attribuzione (vuoti = OpenStreetMap).</summary>
public partial class MapSettingsCard
{
    [Inject] private VedettaVipApiClient Api { get; set; } = default!;

    private bool loaded;
    private bool busy;
    private string? error;
    private DateTime? savedAt;
    private string tileUrl = "";
    private string attribution = "";
    private string attributionUrl = "";

    protected override async Task OnInitializedAsync()
    {
        try
        {
            Show(await Api.GetMapSettingsAsync(CancellationToken.None));
            loaded = true;
        }
        catch (HttpRequestException ex)
        {
            error = $"Impostazioni delle mappe non caricate: {ex.Message}";
        }
    }

    private void Show(MapSettingsDto s) =>
        (tileUrl, attribution, attributionUrl) = (s.TileUrl ?? "", s.Attribution ?? "", s.AttributionUrl ?? "");

    private Task SaveAsync() => SaveAsync(new MapSettingsDto(tileUrl, attribution, attributionUrl));

    private Task ResetAsync() => SaveAsync(MapSettingsDto.Default);

    private async Task SaveAsync(MapSettingsDto dto)
    {
        busy = true;
        error = null;
        savedAt = null;
        try
        {
            Show(await Api.UpdateMapSettingsAsync(dto, CancellationToken.None));
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
