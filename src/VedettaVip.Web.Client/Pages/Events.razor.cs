// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Pages;

/// <summary>
/// Eventi (cambi di stato, agenti offline/online) con esito delle notifiche e presa in carico.
/// Filtri per cliente, dispositivo e periodo applicati dall'API (la paginazione resta corretta).
/// Si aggiorna ogni 30 s mantenendo le pagine già caricate.
/// </summary>
public partial class Events : IDisposable
{
    private const int PageSize = 100;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    /// <summary>Valore del filtro cliente per "senza cliente" (comprende gli eventi degli agenti).</summary>
    private const string NoneFilter = "none";
    private const string AllRange = "all";
    private const string CustomRange = "custom";

    /// <summary>Periodi proposti: gli ultimi N si spostano con l'ora a ogni aggiornamento.</summary>
    private static readonly (string Key, string Label, TimeSpan? Span)[] Ranges =
    [
        (AllRange, "Tutto il periodo", null),
        ("1h", "Ultima ora", TimeSpan.FromHours(1)),
        ("24h", "Ultime 24 ore", TimeSpan.FromHours(24)),
        ("7d", "Ultimi 7 giorni", TimeSpan.FromDays(7)),
        ("30d", "Ultimi 30 giorni", TimeSpan.FromDays(30)),
        (CustomRange, "Intervallo personalizzato", null)
    ];

    [Inject] private VedettaVipApiClient Api { get; set; } = default!;
    [Inject] private CurrentUser User { get; set; } = default!;

    private readonly CancellationTokenSource cts = new();
    private List<EventDto>? events;
    private bool unacknowledgedOnly;
    private bool hasMore;
    private bool loading;
    private string? error;
    private long? expandedId;
    private IReadOnlyList<DeliveryDto>? deliveries;

    private IReadOnlyList<CustomerDto> customers = [];
    private IReadOnlyList<DeviceDto> devices = [];
    private string customerFilter = "";
    private Guid? deviceFilter;
    private string severityFilter = ""; // stringa: il select legato a un enum nullable non è affidabile

    /// <summary>Tipi del filtro, dal più grave.</summary>
    private static readonly EventSeverity[] Severities =
        [EventSeverity.Critical, EventSeverity.Error, EventSeverity.Warning, EventSeverity.Info];
    private string range = AllRange;
    private DateTime? customFrom; // ora locale del browser (input datetime-local)
    private DateTime? customTo;   // vuoto = fino ad adesso

    /// <summary>Numero dell'ultimo caricamento: le risposte di caricamenti superati (filtri cambiati nel frattempo) si scartano.</summary>
    private int loadGeneration;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            await User.GetAsync();
        }
        catch (HttpRequestException)
        {
            // gestito dal caricamento degli eventi
        }
        await LoadFilterChoicesAsync();
        await ReloadAsync();
        _ = RefreshLoopAsync(cts.Token);
    }

    private async Task ReloadAsync() => await LoadAsync(replace: true, before: null, limit: PageSize);

    /// <summary>Aggiornamento periodico: ricarica tanti eventi quanti ne sono già visibili.</summary>
    private async Task RefreshLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(RefreshInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await InvokeAsync(async () =>
                {
                    await LoadAsync(replace: true, before: null, limit: Math.Max(PageSize, events?.Count ?? 0));
                    StateHasChanged();
                });
        }
        catch (OperationCanceledException) { }
    }

    private Task LoadMoreAsync() => LoadAsync(replace: false, before: events?.LastOrDefault()?.Time, limit: PageSize);

    /// <summary>Clienti e dispositivi per i filtri; senza, i filtri restano vuoti ma la pagina funziona.</summary>
    private async Task LoadFilterChoicesAsync()
    {
        try
        {
            customers = [.. (await Api.GetCustomersAsync(cts.Token)).OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)];
            devices = [.. (await Api.GetDevicesAsync(cts.Token)).OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)];
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (HttpRequestException ex)
        {
            error = $"Filtri non caricati: {ex.Message}";
        }
    }

    /// <summary>Dispositivi proposti nel filtro: solo quelli del cliente scelto, se c'è.</summary>
    private IEnumerable<DeviceDto> DeviceChoices() => customerFilter switch
    {
        NoneFilter => devices.Where(d => d.CustomerId is null),
        _ when Guid.TryParse(customerFilter, out var id) => devices.Where(d => d.CustomerId == id),
        _ => devices
    };

    private async Task SetCustomerAsync(ChangeEventArgs e)
    {
        customerFilter = e.Value?.ToString() ?? "";
        // un dispositivo di un altro cliente darebbe sempre un elenco vuoto
        if (deviceFilter is { } id && !DeviceChoices().Any(d => d.Id == id))
            deviceFilter = null;
        await ReloadAsync();
    }

    private async Task SetDeviceAsync(ChangeEventArgs e)
    {
        deviceFilter = Guid.TryParse(e.Value?.ToString(), out var id) ? id : null;
        await ReloadAsync();
    }

    private async Task SetRangeAsync()
    {
        // intervallo personalizzato: si parte dalle ultime 24 ore, da correggere a mano
        if (range == CustomRange && customFrom is null && customTo is null)
        {
            var now = DateTime.Now;
            customFrom = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0).AddDays(-1);
        }
        await ReloadAsync();
    }

    private bool FiltersActive => customerFilter.Length > 0 || deviceFilter is not null || severityFilter.Length > 0 || range != AllRange;

    private async Task ClearFiltersAsync()
    {
        customerFilter = "";
        deviceFilter = null;
        severityFilter = "";
        range = AllRange;
        customFrom = customTo = null;
        await ReloadAsync();
    }

    /// <summary>Inizio del periodo scelto (null = nessun limite).</summary>
    private DateTimeOffset? Since() => range switch
    {
        CustomRange => ToOffset(customFrom),
        _ => Ranges.FirstOrDefault(r => r.Key == range).Span is { } span ? DateTimeOffset.UtcNow - span : null
    };

    /// <summary>Fine del periodo (solo per l'intervallo personalizzato; null = fino ad adesso).</summary>
    private DateTimeOffset? Until() => range == CustomRange ? ToOffset(customTo) : null;

    private static DateTimeOffset? ToOffset(DateTime? local) =>
        local is { } t ? new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Local)) : null;

    private async Task LoadAsync(bool replace, DateTimeOffset? before, int limit)
    {
        var generation = ++loadGeneration;
        loading = true;
        try
        {
            Guid? customerId = Guid.TryParse(customerFilter, out var cid) ? cid : null;
            var page = await Api.GetEventsAsync(unacknowledgedOnly, replace ? before ?? Until() : before, limit, cts.Token,
                since: Since(), deviceId: deviceFilter, customerId: customerId, noCustomer: customerFilter == NoneFilter,
                severity: Enum.TryParse<EventSeverity>(severityFilter, out var sev) ? sev : null);
            if (generation != loadGeneration)
                return;
            if (replace || events is null)
                events = [.. page];
            else
                events.AddRange(page);
            hasMore = page.Count == limit;
            error = null;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (HttpRequestException ex)
        {
            if (generation == loadGeneration)
                error = $"Eventi non caricati: {ex.Message}";
        }
        finally
        {
            if (generation == loadGeneration)
                loading = false;
        }
    }

    private async Task AcknowledgeAsync(EventDto e)
    {
        try
        {
            await Api.AcknowledgeEventAsync(e.Id, !e.Acknowledged, cts.Token);
            var index = events!.FindIndex(x => x.Id == e.Id);
            if (unacknowledgedOnly && !e.Acknowledged)
                events.RemoveAt(index);
            else
                events[index] = e.Acknowledged
                    ? e with { Acknowledged = false, ResolvedAt = null, AcknowledgedBy = null, AcknowledgedAt = null }
                    : e with { Acknowledged = true, AcknowledgedBy = User.Value?.DisplayName, AcknowledgedAt = DateTimeOffset.Now };
        }
        catch (HttpRequestException ex)
        {
            error = ex.Message;
        }
    }

    private async Task ToggleDeliveriesAsync(EventDto e)
    {
        if (expandedId == e.Id)
        {
            expandedId = null;
            return;
        }

        expandedId = e.Id;
        deliveries = null;
        try
        {
            deliveries = await Api.GetDeliveriesAsync(e.Id, cts.Token);
        }
        catch (HttpRequestException ex)
        {
            error = ex.Message;
            deliveries = [];
        }
    }

    private int OpenCount() => events?.Count(e => !e.Acknowledged) ?? 0;

    internal static string SeverityText(EventSeverity s) => s switch
    {
        EventSeverity.Critical => "critico",
        EventSeverity.Error => "errore",
        EventSeverity.Warning => "avviso",
        _ => "info"
    };

    internal static string ChangeText(EventDto e) => e.Type switch
    {
        "AgentOffline" => "agente offline",
        "AgentOnline" => "agente online",
        "ThresholdRaised" or "ThresholdCleared" => AfterName(e.Message),
        _ when e.FromState is { } from && e.ToState is { } to => $"{State(from)} → {State(to)}",
        _ => e.Message
    };

    private static string State(NodeState s) => s.ToString();

    /// <summary>Il messaggio delle soglie inizia con "Nome (indirizzo): ": in tabella basta il resto.</summary>
    private static string AfterName(string message) =>
        message.IndexOf("): ", StringComparison.Ordinal) is var i and >= 0 ? message[(i + 3)..] : message;

    /// <summary>"risolto alle 14:32 (dopo 12 min)" per i problemi chiusi dal ripristino.</summary>
    private static string? ResolvedText(EventDto e) => e.ResolvedAt is { } at
        ? $"risolto alle {at.ToLocalTime():HH:mm} (dopo {Duration(at - e.Time)})"
        : null;

    internal static string Duration(TimeSpan d) => d switch
    {
        { TotalMinutes: < 1 } => $"{Math.Max(0, (int)d.TotalSeconds)} s",
        { TotalHours: < 1 } => $"{(int)d.TotalMinutes} min",
        { TotalDays: < 1 } => $"{(int)d.TotalHours} h {d.Minutes} min",
        _ => $"{(int)d.TotalDays} g {d.Hours} h"
    };

    private static string NotifyText(EventDto e) => e.NotifyState switch
    {
        NotifyState.Pending => "in attesa (ritardo di invio)",
        NotifyState.Suppressed => $"soppressa: {e.NotifyNote}",
        NotifyState.Maintenance => $"rimandata: {e.NotifyNote}",
        NotifyState.None => "-",
        _ when e.DeliveriesFailed > 0 => $"{e.DeliveriesSent} inviate, {e.DeliveriesFailed} fallite",
        _ when e.DeliveriesPending > 0 => $"{e.DeliveriesSent} inviate, {e.DeliveriesPending} in corso",
        _ when e.DeliveriesSent > 0 => $"{e.DeliveriesSent} inviate",
        _ => e.NotifyNote ?? "-"
    };

    private static string DeliveryText(DeliveryDto d) => d.Status switch
    {
        DeliveryStatus.Sent => $"inviata {d.SentAt?.ToLocalTime():HH:mm:ss}",
        DeliveryStatus.Failed => "fallita",
        _ => "in corso (nuovo tentativo)"
    };

    public void Dispose()
    {
        cts.Cancel();
        cts.Dispose();
    }
}
