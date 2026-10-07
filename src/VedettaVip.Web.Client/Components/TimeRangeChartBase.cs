// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using VedettaVip.Shared.Contracts;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Components;

/// <summary>
/// Base dei grafici storici: periodo selezionato (1h…30g), caricamento con gestione errori, ricarica quando
/// cambiano i parametri (<see cref="LoadKey"/>) e aggiornamento automatico ogni 60 s per i periodi fino a 24 h.
/// </summary>
public abstract class TimeRangeChartBase : ComponentBase, IDisposable
{
    protected static readonly (string Label, TimeSpan Range)[] Ranges =
    [
        ("1h", TimeSpan.FromHours(1)), ("6h", TimeSpan.FromHours(6)), ("24h", TimeSpan.FromHours(24)),
        ("7g", TimeSpan.FromDays(7)), ("30g", TimeSpan.FromDays(30))
    ];

    private static readonly TimeSpan AutoRefresh = TimeSpan.FromSeconds(60);

    [Inject] protected VedettaVipApiClient Api { get; set; } = default!;

    protected readonly CancellationTokenSource Cts = new();
    protected TimeSpan SelectedRange { get; private set; } = TimeSpan.FromHours(6);
    protected bool Loading { get; private set; }
    protected string? Error { get; private set; }
    /// <summary>Cambia a ogni caricamento: usato come @key per ricreare il grafico con i nuovi punti.</summary>
    protected int RenderKey { get; private set; }

    private object? loadedFor;
    private bool refreshStarted;

    /// <summary>Identità dei parametri: quando cambia (altro device o interfaccia) il grafico si ricarica.</summary>
    protected abstract object LoadKey { get; }

    /// <summary>Carica i dati del periodo; le eccezioni HTTP sono gestite dalla base.</summary>
    protected abstract Task LoadCoreAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    protected override async Task OnParametersSetAsync()
    {
        if (!Equals(loadedFor, LoadKey))
        {
            loadedFor = LoadKey;
            await LoadAsync();
        }

        if (!refreshStarted)
        {
            refreshStarted = true;
            _ = RefreshLoopAsync(Cts.Token);
        }
    }

    protected async Task SelectRangeAsync(TimeSpan range)
    {
        SelectedRange = range;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        Loading = true;
        Error = null;
        try
        {
            var to = DateTimeOffset.UtcNow;
            await LoadCoreAsync(to - SelectedRange, to, Cts.Token);
            RenderKey++;
        }
        catch (OperationCanceledException) when (Cts.IsCancellationRequested)
        {
        }
        catch (HttpRequestException ex)
        {
            Error = $"Storico non disponibile: {ex.Message}";
        }
        finally
        {
            Loading = false;
        }
    }

    private async Task RefreshLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(AutoRefresh);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (SelectedRange > TimeSpan.FromHours(24))
                    continue; // su 7/30 giorni un minuto in più non cambia il grafico
                await InvokeAsync(async () =>
                {
                    await LoadAsync();
                    StateHasChanged();
                });
            }
        }
        catch (OperationCanceledException) { }
    }

    protected static string ResolutionText(int bucketSeconds, MetricSource source) => bucketSeconds switch
    {
        < 60 => $"{bucketSeconds} s",
        < 3600 => $"{bucketSeconds / 60} min",
        < 86400 => $"{bucketSeconds / 3600} h",
        _ => $"{bucketSeconds / 86400} g"
    } + (source == MetricSource.Raw ? "" : " (aggregato)");

    protected static decimal? ToDecimal(double? v) => v is { } x ? (decimal)x : null;

    /// <summary>95° percentile (metodo nearest-rank); null se non ci sono valori.</summary>
    protected static double? Percentile95(IEnumerable<double> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count == 0 ? null : sorted[Math.Clamp((int)Math.Ceiling(sorted.Count * 0.95) - 1, 0, sorted.Count - 1)];
    }

    public virtual void Dispose()
    {
        Cts.Cancel();
        Cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
