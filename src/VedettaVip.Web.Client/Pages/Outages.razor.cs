// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using VedettaVip.Shared.Contracts;
using VedettaVip.Web.Client.Components;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Pages;

/// <summary>
/// Vista globale delle interruzioni (come il pannello Outages di The Dude): tutte quelle dei dispositivi, con filtri per
/// stato, cliente, servizio e periodo, ricerca, ordinamento per colonna, durate dal vivo, export CSV e grafici del device.
/// Formati e testi sono quelli della scheda Interruzioni del dispositivo (<see cref="DeviceOutages"/>).
/// </summary>
public partial class Outages : IDisposable
{
    [Inject] private VedettaVipApiClient Api { get; set; } = default!;
    [Inject] private ILogger<Outages> Logger { get; set; } = default!;

    /// <summary>Filtri iniziali dall'indirizzo, per i link da altre pagine (/outages?status=Active&amp;customer=…).</summary>
    [SupplyParameterFromQuery(Name = "status")] public string? StatusQuery { get; set; }
    [SupplyParameterFromQuery(Name = "customer")] public string? CustomerQuery { get; set; }

    private const string NoneFilter = "none";
    private const int Limit = 2000; // massimo dell'API
    private const int RefreshEverySeconds = 30;

    private enum SortColumn { Status, Start, Duration, Device, Customer, Service }

    private static readonly OutageService[] Services = Enum.GetValues<OutageService>();

    private readonly CancellationTokenSource cts = new();
    private IReadOnlyList<CustomerDto> customers = [];
    private List<OutageDto>? outages;
    private string? error;
    /// <summary>Riga da cui sono stati aperti i grafici del dispositivo (resta aperta anche se una ricarica la toglie).</summary>
    private OutageDto? chartsOutage;

    // Filtri legati ai <select> come stringhe (vedi CLAUDE.md: i select su tipi non stringa non si agganciano bene)
    private string status = nameof(OutageStatusFilter.All);
    private string customer = "";
    private string service = "";
    private string period = "30";
    private string search = "";
    private SortColumn sort = SortColumn.Start;
    private bool descending = true;

    private bool FiltersActive => status != nameof(OutageStatusFilter.All) || customer.Length > 0 || service.Length > 0
        || period != "30" || search.Trim().Length > 0;

    protected override async Task OnInitializedAsync()
    {
        if (Enum.TryParse<OutageStatusFilter>(StatusQuery, ignoreCase: true, out var s))
            status = s.ToString();
        if (CustomerQuery is NoneFilter || Guid.TryParse(CustomerQuery, out _))
            customer = CustomerQuery!;

        try
        {
            customers = [.. (await Api.GetCustomersAsync(cts.Token)).OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)];
        }
        catch (Exception ex) when (ex is HttpRequestException or ApiProblemException)
        {
            Logger.LogWarning("Clienti non caricati: {Message}", ex.Message); // la pagina funziona anche senza il filtro
        }

        await LoadAsync();
        _ = TickAsync(cts.Token);
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
        try
        {
            var (customerId, noCustomer) = CustomerFilter();
            var list = await Api.GetOutagesAsync(null, Enum.Parse<OutageStatusFilter>(status), SelectedService, From(),
                Limit, cts.Token, customerId, noCustomer);
            outages = [.. list];
            error = null;
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

    private Task ReloadAsync() => LoadAsync();

    private async Task ResetFiltersAsync()
    {
        (status, customer, service, period, search) = (nameof(OutageStatusFilter.All), "", "", "30", "");
        await LoadAsync();
    }

    private (Guid? CustomerId, bool NoCustomer) CustomerFilter() =>
        customer == NoneFilter ? (null, true) : Guid.TryParse(customer, out var id) ? (id, false) : (null, false);

    private OutageService? SelectedService => Enum.TryParse<OutageService>(service, out var s) ? s : null;

    private DateTimeOffset? From()
    {
        var days = DeviceOutages.Periods.First(p => p.Value == period).Days;
        return days > 0 ? DateTimeOffset.UtcNow.AddDays(-days) : null;
    }

    /// <summary>CSV con gli stessi filtri della tabella, ricerca esclusa (periodo calcolato a ogni ridisegno).</summary>
    private string ExportUrl
    {
        get
        {
            var (customerId, noCustomer) = CustomerFilter();
            return Api.OutagesExportUrl(null, Enum.Parse<OutageStatusFilter>(status), SelectedService, From(), customerId, noCustomer);
        }
    }

    private int ActiveCount() => outages?.Count(o => o.EndedAt is null) ?? 0;

    /// <summary>Righe che passano la ricerca (nome, indirizzo, cliente, dettaglio), fatta nel browser.</summary>
    private IEnumerable<OutageDto> Visible()
    {
        if (outages is null)
            return [];
        var text = search.Trim();
        return text.Length == 0
            ? outages
            : outages.Where(o => Contains(o.DeviceName, text) || Contains(o.DeviceAddress, text)
                || Contains(o.CustomerName, text) || Contains(o.Detail, text));

        static bool Contains(string? value, string text) => value?.Contains(text, StringComparison.CurrentCultureIgnoreCase) == true;
    }

    private void SortBy(SortColumn column)
    {
        if (sort == column)
            descending = !descending;
        else
            (sort, descending) = (column, column is SortColumn.Start or SortColumn.Duration);
    }

    private IEnumerable<OutageDto> Sorted(DateTimeOffset now)
    {
        var rows = Visible();
        var names = StringComparer.CurrentCultureIgnoreCase;
        IOrderedEnumerable<OutageDto> ordered = sort switch
        {
            SortColumn.Status => descending ? rows.OrderBy(o => o.EndedAt is null ? 0 : 1) : rows.OrderBy(o => o.EndedAt is null ? 1 : 0),
            SortColumn.Duration => descending
                ? rows.OrderByDescending(o => DeviceOutages.DurationOf(o, now))
                : rows.OrderBy(o => DeviceOutages.DurationOf(o, now)),
            SortColumn.Device => descending ? rows.OrderByDescending(o => o.DeviceName, names) : rows.OrderBy(o => o.DeviceName, names),
            SortColumn.Customer => descending
                ? rows.OrderByDescending(o => o.CustomerName ?? "", names)
                : rows.OrderBy(o => o.CustomerName ?? "", names),
            SortColumn.Service => descending
                ? rows.OrderByDescending(o => DeviceOutages.ServiceLabel(o.Service))
                : rows.OrderBy(o => DeviceOutages.ServiceLabel(o.Service)),
            _ => descending ? rows.OrderByDescending(o => o.StartedAt) : rows.OrderBy(o => o.StartedAt)
        };
        // A parità, le più recenti prima
        return ordered.ThenByDescending(o => o.StartedAt);
    }

    private string SortMark(SortColumn column) => sort != column ? "" : descending ? " ▾" : " ▴";

    public void Dispose()
    {
        cts.Cancel();
        cts.Dispose();
    }
}
