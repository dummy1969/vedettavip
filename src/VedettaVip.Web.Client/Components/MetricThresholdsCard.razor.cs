// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using VedettaVip.Shared.Contracts;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Components;

/// <summary>Soglie generali su latenza, perdita e utilizzo dei link (campo vuoto = soglia disattivata).</summary>
public partial class MetricThresholdsCard
{
    [Inject] private VedettaVipApiClient Api { get; set; } = default!;

    private bool loaded;
    private bool busy;
    private string? error;
    private DateTime? savedAt;
    private int? rttMs;
    private double? lossPct;
    private int? linkPct;
    private int windowMinutes = 5;
    private int? cpuPct;
    private int? temperatureC;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var t = await Api.GetMetricThresholdsAsync(CancellationToken.None);
            (rttMs, lossPct, linkPct, windowMinutes, cpuPct, temperatureC) =
                (t.RttMs, t.LossPct, t.LinkUtilizationPct, t.WindowMinutes, t.CpuPct, t.TemperatureC);
            loaded = true;
        }
        catch (HttpRequestException ex)
        {
            error = $"Soglie non caricate: {ex.Message}";
        }
    }

    private async Task SaveAsync()
    {
        busy = true;
        error = null;
        savedAt = null;
        try
        {
            await Api.UpdateMetricThresholdsAsync(new MetricThresholdsDto(rttMs, lossPct, linkPct, windowMinutes, cpuPct, temperatureC), CancellationToken.None);
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
