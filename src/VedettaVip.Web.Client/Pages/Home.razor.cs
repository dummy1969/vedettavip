// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Pages;

/// <summary>
/// Dashboard iniziale (stile NOC): riepilogo dello stato dei dispositivi, elenco di quelli Down/Partial con le mappe in
/// cui compaiono, eventi delle ultime ore (periodo predefinito in Impostazioni). Aggiornata ogni 30 s.
/// </summary>
public partial class Home : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);
    private const int EventLimit = 500;
    private const double R = 46;
    private static readonly double Circumference = 2 * Math.PI * R;

    private static readonly (NodeState State, string Label)[] Legend =
    [
        (NodeState.Up, "Up"),
        (NodeState.Partial, "Partial"),
        (NodeState.Down, "Down"),
        (NodeState.Unknown, "Sconosciuti")
    ];

    [Inject] private VedettaVipApiClient Api { get; set; } = default!;
    [Inject] private ILogger<Home> Logger { get; set; } = default!;

    private readonly CancellationTokenSource cts = new();
    private List<DeviceOverviewDto>? rows;
    private List<EventDto>? events;
    private Dictionary<Guid, string> customers = [];
    private List<AgentDto> offlineAgents = [];
    private string? loadError;
    private DateTime? updatedAt;
    private int defaultEventHours = DashboardSettingsDto.Default.RecentEventHours;
    private int eventHours = DashboardSettingsDto.Default.RecentEventHours;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            defaultEventHours = eventHours = (await Api.GetDashboardSettingsAsync(cts.Token)).RecentEventHours;
            customers = (await Api.GetCustomersAsync(cts.Token)).ToDictionary(c => c.Id, c => c.Name);
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning("Impostazioni della dashboard o clienti non caricati: {Message}", ex.Message); // valori predefiniti
        }

        await RefreshAsync();
        _ = RefreshLoopAsync(cts.Token);
    }

    private async Task RefreshAsync()
    {
        try
        {
            rows = [.. await Api.GetDeviceOverviewAsync(cts.Token)];
            offlineAgents = [.. (await Api.GetAgentsAsync(cts.Token)).Where(a => !a.IsOnline).OrderBy(a => a.AgentId)];
            loadError = null;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            return;
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "Caricamento della dashboard fallito");
            loadError = $"API non raggiungibile o in errore ({ex.StatusCode?.ToString() ?? ex.Message}).";
        }

        await LoadEventsAsync();
        updatedAt = DateTime.Now;
    }

    private async Task LoadEventsAsync()
    {
        try
        {
            var since = DateTimeOffset.UtcNow.AddHours(-eventHours);
            events = [.. await Api.GetEventsAsync(false, null, EventLimit, cts.Token, since)];
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "Caricamento degli eventi fallito");
            loadError = $"Eventi non caricati ({ex.StatusCode?.ToString() ?? ex.Message}).";
        }
    }

    private async Task RefreshLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(RefreshInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await InvokeAsync(async () =>
                {
                    await RefreshAsync();
                    StateHasChanged();
                });
        }
        catch (OperationCanceledException) { }
    }

    // ---------- Riepilogo: solo i dispositivi abilitati (i disabilitati sono contati a parte) ----------

    private IEnumerable<DeviceOverviewDto> Monitored => rows?.Where(r => r.Device.Enabled) ?? [];
    private int MonitoredCount => Monitored.Count();
    private int Count(NodeState state) => Monitored.Count(r => r.State == state);
    private int MaintenanceCount => Monitored.Count(r => r.Maintenance is not null);
    private int DisabledCount => rows?.Count(r => !r.Device.Enabled) ?? 0;

    private record Segment(string Color, string DashArray, string DashOffset, string Title);

    /// <summary>Archi della ciambella: un cerchio per stato con stroke-dasharray, uno dopo l'altro in senso orario.</summary>
    private IEnumerable<Segment> Segments()
    {
        var total = MonitoredCount;
        if (total == 0) yield break;

        double offset = 0;
        foreach (var (state, label) in Legend)
        {
            var n = Count(state);
            if (n == 0) continue;
            var len = Circumference * n / total;
            yield return new Segment(StateColor(state),
                $"{F(len)} {F(Circumference - len)}", F(-offset), $"{label}: {n}");
            offset += len;
        }
    }

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private string UpPercentText() => MonitoredCount == 0 ? "-" : $"{Math.Floor(100.0 * Count(NodeState.Up) / MonitoredCount):0}%";

    private string HealthClass() => Count(NodeState.Down) > 0 ? "down" : Count(NodeState.Partial) > 0 ? "partial" : "up";

    // ---------- Problemi ----------

    /// <summary>Down prima dei Partial; a parità, i problemi più recenti in alto.</summary>
    private IEnumerable<DeviceOverviewDto> Problems() => Monitored
        .Where(r => r.State is NodeState.Down or NodeState.Partial)
        .OrderBy(r => r.State == NodeState.Down ? 0 : 1)
        .ThenByDescending(r => r.Since ?? DateTimeOffset.MinValue);

    private string? CustomerName(Guid? id) => id is { } c && customers.TryGetValue(c, out var name) ? name : null;

    // ---------- Eventi ----------

    private IEnumerable<int> HourOptions() => new[] { 1, 4, 12, 24, 72, 168, defaultEventHours }.Distinct().Order();

    private static string HoursText(int h) => h switch
    {
        1 => "1 ora",
        168 => "7 giorni",
        _ when h % 24 == 0 => $"{h / 24} giorni",
        _ => $"{h} ore"
    };

    /// <summary>Tempo trascorso compatto: "45 s", "12 m", "3 h", "2 g".</summary>
    private static string Ago(DateTimeOffset time)
    {
        var d = DateTimeOffset.UtcNow - time;
        return d switch
        {
            { TotalMinutes: < 1 } => $"{Math.Max(0, (int)d.TotalSeconds)} s",
            { TotalHours: < 1 } => $"{(int)d.TotalMinutes} m",
            { TotalDays: < 1 } => $"{(int)d.TotalHours} h",
            _ => $"{(int)d.TotalDays} g"
        };
    }

    // Stessi colori della mappa e della pagina Dispositivi
    private static string StateColor(NodeState s) => s switch
    {
        NodeState.Up => "#8fd18f",
        NodeState.Partial => "#f5c06b",
        NodeState.Down => "#f08080",
        _ => "#c8c8c8"
    };

    private static string StateClass(NodeState s) => s.ToString().ToLowerInvariant();

    public void Dispose()
    {
        cts.Cancel();
        cts.Dispose();
    }
}
