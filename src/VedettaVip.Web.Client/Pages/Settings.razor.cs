// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using VedettaVip.Shared.Contracts;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Pages;

/// <summary>Impostazioni generali: soglie di rilevazione dello stato (isteresi) valide per tutti i device.</summary>
public partial class Settings : IDisposable
{
    /// <summary>Intervallo di polling del Worker (Polling:IntervalSeconds): serve solo a tradurre le soglie in tempo.</summary>
    private const int PollSeconds = 30;

    [Inject] private VedettaVipApiClient Api { get; set; } = default!;

    private readonly CancellationTokenSource cts = new();
    private bool loaded;
    private bool saving;
    private string? loadError;
    private string? saveError;
    private DateTime? savedAt;

    private int downAfterFailures;
    private int upAfterSuccesses;
    private int snmpDegradedAfterFailures;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var s = await Api.GetDetectionSettingsAsync(cts.Token);
            (downAfterFailures, upAfterSuccesses, snmpDegradedAfterFailures) = (s.DownAfterFailures, s.UpAfterSuccesses, s.SnmpDegradedAfterFailures);
            loaded = true;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (HttpRequestException ex)
        {
            loadError = $"Impostazioni non caricate: {ex.Message}";
        }
    }

    private async Task SaveAsync()
    {
        saving = true;
        saveError = null;
        savedAt = null;
        try
        {
            await Api.UpdateDetectionSettingsAsync(new DetectionThresholdsDto(downAfterFailures, upAfterSuccesses, snmpDegradedAfterFailures), cts.Token);
            savedAt = DateTime.Now;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (HttpRequestException ex)
        {
            saveError = ex.Message;
        }
        finally
        {
            saving = false;
        }
    }

    /// <summary>Es. "≈ 90 s" oppure "≈ 2,5 min".</summary>
    internal static string Delay(int polls)
    {
        if (polls < 1) return "";
        var seconds = polls * PollSeconds;
        return seconds < 120 ? $"≈ {seconds} s" : $"≈ {seconds / 60.0:0.#} min";
    }

    public void Dispose()
    {
        cts.Cancel();
        cts.Dispose();
    }
}
