// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using VedettaVip.Shared.Contracts;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Components;

/// <summary>
/// Storico dei down di un device (scheda "Interruzioni" dei grafici, come Outages di The Dude): stato, inizio, durata
/// (dal vivo per quelle in corso), servizio e dettaglio, filtri per stato, servizio e periodo, ordinamento per colonna,
/// riepilogo di disponibilità e "Rimuovi risolte" (Operatore).
/// </summary>
public partial class DeviceOutages : IDisposable
{
    [Inject] private VedettaVipApiClient Api { get; set; } = default!;
    [Inject] private CurrentUser User { get; set; } = default!;
    [Inject] private ILogger<DeviceOutages> Logger { get; set; } = default!;

    [Parameter, EditorRequired] public Guid DeviceId { get; set; }
    /// <summary>Numero di interruzioni in corso, dopo ogni caricamento (badge della scheda).</summary>
    [Parameter] public EventCallback<int> OnActiveChanged { get; set; }

    private const int Limit = 1000;
    private const int RefreshEverySeconds = 30;
    private const int SummaryDaysForAll = 365;

    private enum SortColumn { Status, Start, Duration, Service }

    private static readonly (string Value, string Label, int Days)[] Periods =
    [
        ("1", "ultime 24 ore", 1), ("7", "ultimi 7 giorni", 7), ("30", "ultimi 30 giorni", 30), ("90", "ultimi 90 giorni", 90),
        ("365", "ultimo anno", 365), ("all", "tutto lo storico", 0)
    ];

    private static readonly OutageService[] Services = Enum.GetValues<OutageService>();

    private readonly CancellationTokenSource cts = new();
    private Guid? loadedFor;
    private bool loopStarted;
    private bool canEdit;

    // Filtri legati ai <select> come stringhe (vedi CLAUDE.md: i select su tipi non stringa non si agganciano bene)
    private string status = nameof(OutageStatusFilter.All);
    private string service = "";
    private string period = "all";

    private List<OutageDto>? outages;
    private OutageSummaryDto? summary;
    private string? error;
    private string? notice;
    private bool busy;
    private SortColumn sort = SortColumn.Start;
    private bool descending = true;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            canEdit = (await User.GetAsync(cts.Token)) is not null && User.CanEdit;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            canEdit = false;
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (loadedFor == DeviceId)
            return;
        loadedFor = DeviceId;
        outages = null;
        summary = null;
        notice = null;
        await LoadAsync();
    }

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender && !loopStarted)
        {
            loopStarted = true;
            _ = TickAsync(cts.Token);
        }
    }

    /// <summary>Ogni secondo ridisegna le durate in corso, ogni 30 s ricarica dal server.</summary>
    private async Task TickAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var seconds = 0;
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (++seconds % RefreshEverySeconds == 0)
                    await InvokeAsync(async () =>
                    {
                        await LoadAsync();
                        StateHasChanged();
                    });
                else if (outages?.Any(o => o.EndedAt is null) == true)
                    await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task LoadAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var days = Periods.First(p => p.Value == period).Days;
        try
        {
            var list = await Api.GetOutagesAsync(DeviceId, Enum.Parse<OutageStatusFilter>(status), SelectedService,
                days > 0 ? now.AddDays(-days) : null, Limit, cts.Token);
            summary = await Api.GetAvailabilityAsync(DeviceId, now.AddDays(-(days > 0 ? days : SummaryDaysForAll)), cts.Token);
            outages = [.. list];
            error = null;
            await OnActiveChanged.InvokeAsync(summary.Active);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is HttpRequestException or ApiProblemException)
        {
            Logger.LogWarning("Interruzioni non caricate: {Message}", ex.Message);
            error = $"Interruzioni non caricate: {ex.Message}";
            outages ??= [];
        }
    }

    /// <summary>CSV con gli stessi filtri della tabella (periodo calcolato a ogni ridisegno, cioè almeno ogni 30 s).</summary>
    private string ExportUrl
    {
        get
        {
            var days = Periods.First(p => p.Value == period).Days;
            return Api.OutagesExportUrl(DeviceId, Enum.Parse<OutageStatusFilter>(status), SelectedService,
                days > 0 ? DateTimeOffset.UtcNow.AddDays(-days) : null);
        }
    }

    private OutageService? SelectedService => Enum.TryParse<OutageService>(service, out var s) ? s : null;

    private Task FiltersChangedAsync()
    {
        notice = null;
        return LoadAsync();
    }

    private async Task RemoveResolvedAsync()
    {
        busy = true;
        try
        {
            var result = await Api.DeleteResolvedOutagesAsync(DeviceId, SelectedService, cts.Token);
            notice = result.Deleted switch
            {
                0 => "Nessuna interruzione risolta da rimuovere.",
                1 => "Rimossa 1 interruzione risolta.",
                var n => $"Rimosse {n} interruzioni risolte."
            };
            await LoadAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or ApiProblemException)
        {
            error = $"Rimozione non riuscita: {ex.Message}";
        }
        finally
        {
            busy = false;
        }
    }

    private void SortBy(SortColumn column)
    {
        if (sort == column)
            descending = !descending;
        else
            (sort, descending) = (column, column is SortColumn.Start or SortColumn.Duration);
    }

    private IEnumerable<OutageDto> Sorted
    {
        get
        {
            if (outages is null)
                return [];
            var now = DateTimeOffset.UtcNow;
            IOrderedEnumerable<OutageDto> ordered = sort switch
            {
                SortColumn.Status => descending ? outages.OrderBy(o => o.EndedAt is null ? 0 : 1) : outages.OrderBy(o => o.EndedAt is null ? 1 : 0),
                SortColumn.Duration => descending ? outages.OrderByDescending(o => DurationOf(o, now)) : outages.OrderBy(o => DurationOf(o, now)),
                SortColumn.Service => descending ? outages.OrderByDescending(o => ServiceLabel(o.Service)) : outages.OrderBy(o => ServiceLabel(o.Service)),
                _ => descending ? outages.OrderByDescending(o => o.StartedAt) : outages.OrderBy(o => o.StartedAt)
            };
            // A parità, le più recenti prima
            return ordered.ThenByDescending(o => o.StartedAt);
        }
    }

    private string SortMark(SortColumn column) => sort != column ? "" : descending ? " ▾" : " ▴";

    private string RemoveConfirmText => SelectedService is { } s
        ? $"Rimuovi tutte le risolte ({ServiceLabel(s)})"
        : "Rimuovi tutte le risolte";

    private string SummaryText(OutageSummaryDto s)
    {
        var days = Periods.First(p => p.Value == period).Days;
        var label = days > 0 ? Periods.First(p => p.Value == period).Label : "ultimo anno";
        var text = $"{char.ToUpper(label[0])}{label[1..]}: {Count(s.Outages, "interruzione", "interruzioni")}";
        if (s.Active > 0)
            text += $" ({s.Active} in corso)";
        text += s.PingOutages == 0
            ? " · mai giù al ping"
            : $" · giù al ping {Readable(TimeSpan.FromSeconds(s.DownSeconds))}";
        if (s.MaintenanceDownSeconds > 0)
            text += $" (+ {Readable(TimeSpan.FromSeconds(s.MaintenanceDownSeconds))} in manutenzione)";
        return text;
    }

    private static string AvailabilityText(OutageSummaryDto s) =>
        $"disponibilità {s.AvailabilityPct.ToString(s.AvailabilityPct >= 99.995 ? "0" : "0.00", CultureInfo.CurrentCulture)} %";

    private static string Count(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";

    private static TimeSpan DurationOf(OutageDto o, DateTimeOffset now) => (o.EndedAt ?? now) - o.StartedAt;

    internal static string DurationText(TimeSpan d) => Labels.Duration(d);

    /// <summary>Durata a parole per il riepilogo: "45 s", "12 min", "3 h 20 min", "2 g 4 h".</summary>
    private static string Readable(TimeSpan d) => d switch
    {
        { TotalMinutes: < 1 } => $"{Math.Max(0, (int)d.TotalSeconds)} s",
        { TotalHours: < 1 } => $"{(int)d.TotalMinutes} min",
        { TotalDays: < 1 } => $"{(int)d.TotalHours} h {d.Minutes} min",
        _ => $"{(int)d.TotalDays} g {d.Hours} h"
    };

    /// <summary>Inizio nell'ora locale; l'anno solo se diverso da quello corrente.</summary>
    private static string StartText(DateTimeOffset at)
    {
        var local = at.ToLocalTime();
        var format = local.Year == DateTimeOffset.Now.Year ? "dd/MM HH:mm:ss" : "dd/MM/yyyy HH:mm:ss";
        return local.ToString(format, CultureInfo.InvariantCulture);
    }

    private static string FullTime(DateTimeOffset at) => at.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);

    private static string RowTitle(OutageDto o) => o.EndedAt is { } end
        ? $"Dal {FullTime(o.StartedAt)} al {FullTime(end)}"
        : $"Dal {FullTime(o.StartedAt)}, ancora in corso";

    internal static string ServiceLabel(OutageService s) => Labels.Service(s);

    public void Dispose()
    {
        cts.Cancel();
        cts.Dispose();
    }
}
