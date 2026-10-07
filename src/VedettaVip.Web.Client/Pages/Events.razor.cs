// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Pages;

/// <summary>
/// Eventi (cambi di stato, agenti offline/online) con esito delle notifiche e presa in carico.
/// Si aggiorna ogni 30 s mantenendo le pagine già caricate.
/// </summary>
public partial class Events : IDisposable
{
    private const int PageSize = 100;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

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

    private async Task LoadAsync(bool replace, DateTimeOffset? before, int limit)
    {
        loading = true;
        try
        {
            var page = await Api.GetEventsAsync(unacknowledgedOnly, before, limit, cts.Token);
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
            error = $"Eventi non caricati: {ex.Message}";
        }
        finally
        {
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
