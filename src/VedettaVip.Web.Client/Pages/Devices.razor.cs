// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Pages;

public partial class Devices : IDisposable
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    [Inject] private VedettaVipApiClient Api { get; set; } = default!;
    [Inject] private ILogger<Devices> Logger { get; set; } = default!;
    [Inject] private CurrentUser User { get; set; } = default!;

    private enum StateFilter { All, Problems, Unknown, Disabled }

    /// <summary>Valore dei filtri cliente/mappa per "senza cliente" / "su nessuna mappa".</summary>
    private const string NoneFilter = "none";

    [Inject] private NavigationManager Navigation { get; set; } = default!;

    /// <summary>Filtri nell'indirizzo (/devices?customer=…&amp;map=…): un link o un segnalibro riapre la stessa vista.</summary>
    [SupplyParameterFromQuery(Name = "customer")] public string? CustomerFilter { get; set; }
    [SupplyParameterFromQuery(Name = "map")] public string? MapFilter { get; set; }

    private IReadOnlyList<MapSummaryDto> maps = [];

    private readonly CancellationTokenSource cts = new();
    private List<DeviceOverviewDto>? rows;
    private string? loadError;
    private string search = "";
    private StateFilter filter = StateFilter.All;

    private bool editorOpen;
    private bool importOpen;
    private Guid? editingId;
    private int editorKey;
    private Guid? deletingId;
    private DeviceInUseDto? deleteResult;

    private IReadOnlyList<DeviceDto> AllDevices => rows?.Select(r => r.Device).ToList() ?? [];
    private Guid? chartsDeviceId;
    private DetectionThresholdsDto? detectionDefaults;
    private IReadOnlyList<CustomerDto> customers = [];
    private IReadOnlyList<SnmpCredentialDto> snmpProfiles = [];
    private IReadOnlyList<RouterOsCredentialDto> routerOsProfiles = [];
    private Dictionary<Guid, RouterOsSampleDto> routerOs = [];
    private MetricThresholdsDto? metricThresholds;
    private DeviceDto? ChartsDevice => rows?.FirstOrDefault(r => r.Device.Id == chartsDeviceId)?.Device;

    private DeviceDto? EditingDevice => rows?.FirstOrDefault(r => r.Device.Id == editingId)?.Device;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            await User.GetAsync();
        }
        catch (HttpRequestException)
        {
            // gestito dal caricamento dell'elenco
        }
        await LoadAsync();
        try
        {
            detectionDefaults = await Api.GetDetectionSettingsAsync(cts.Token);
            customers = await Api.GetCustomersAsync(cts.Token);
            metricThresholds = await Api.GetMetricThresholdsAsync(cts.Token);
            snmpProfiles = await Api.GetSnmpCredentialsAsync(cts.Token);
            maps = await Api.GetMapsAsync(cts.Token);
            routerOsProfiles = await Api.GetRouterOsCredentialsAsync(cts.Token);
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning("Soglie generali o clienti non caricati: {Message}", ex.Message); // il pannello funziona comunque
        }
        _ = RefreshLoopAsync(cts.Token);
    }

    private async Task LoadAsync()
    {
        try
        {
            rows = [.. await Api.GetDeviceOverviewAsync(cts.Token)];
            loadError = null;
            routerOs = (await Api.GetRouterOsAsync(cts.Token)).ToDictionary(s => s.DeviceId);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "Caricamento dei dispositivi fallito");
            loadError = $"API non raggiungibile o in errore ({ex.StatusCode?.ToString() ?? ex.Message}).";
        }
    }

    /// <summary>Stato aggiornato ogni 30 s (stesso ritmo del poller). L'editor aperto non viene toccato.</summary>
    private async Task RefreshLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(RefreshInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await InvokeAsync(async () =>
                {
                    await LoadAsync();
                    StateHasChanged();
                });
        }
        catch (OperationCanceledException) { }
    }

    private IEnumerable<DeviceOverviewDto> Filtered()
    {
        IEnumerable<DeviceOverviewDto> result = rows ?? [];

        var text = search.Trim();
        if (text.Length > 0)
            result = result.Where(r =>
                r.Device.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                r.Device.Address.Contains(text, StringComparison.OrdinalIgnoreCase));

        if (CustomerFilter == NoneFilter)
            result = result.Where(r => r.Device.CustomerId is null);
        else if (Guid.TryParse(CustomerFilter, out var customerId))
            result = result.Where(r => r.Device.CustomerId == customerId);

        if (MapFilter == NoneFilter)
            result = result.Where(r => r.Maps.Count == 0);
        else if (Guid.TryParse(MapFilter, out var mapId))
        {
            // La mappa scelta comprende le sue sottomappe (come le iscrizioni alle notifiche)
            var subtree = MapHierarchy.Subtree(maps, mapId);
            result = result.Where(r => r.Maps.Any(m => subtree.Contains(m.Id)));
        }

        return filter switch
        {
            StateFilter.Problems => result.Where(r => r.State is NodeState.Down or NodeState.Partial),
            StateFilter.Unknown => result.Where(r => r.Device.Enabled && r.State == NodeState.Unknown),
            StateFilter.Disabled => result.Where(r => !r.Device.Enabled),
            _ => result
        };
    }

    private bool FiltersActive => CustomerFilter is { Length: > 0 } || MapFilter is { Length: > 0 } || filter != StateFilter.All || search.Trim().Length > 0;

    /// <summary>Aggiorna l'indirizzo con i filtri cliente/mappa (senza nuova voce nella cronologia).</summary>
    private Task SetFilterAsync(string? customer = null, string? map = null)
    {
        var query = new Dictionary<string, object?>
        {
            ["customer"] = customer is null ? CustomerFilter : customer.Length > 0 ? customer : null,
            ["map"] = map is null ? MapFilter : map.Length > 0 ? map : null
        };
        Navigation.NavigateTo(Navigation.GetUriWithQueryParameters(query), replace: true);
        return Task.CompletedTask;
    }

    private Task ClearFiltersAsync()
    {
        search = "";
        filter = StateFilter.All;
        Navigation.NavigateTo(Navigation.GetUriWithQueryParameters(new Dictionary<string, object?> { ["customer"] = null, ["map"] = null }), replace: true);
        return Task.CompletedTask;
    }

    /// <summary>Conteggio per stato dei soli dispositivi abilitati (i disabilitati sono contati a parte).</summary>
    private int Count(NodeState state) => rows?.Count(r => r.Device.Enabled && r.State == state) ?? 0;

    private int DisabledCount => rows?.Count(r => !r.Device.Enabled) ?? 0;

    private void OpenNew()
    {
        editingId = null;
        editorOpen = true;
        editorKey++;
    }

    private void OpenEdit(DeviceDto device)
    {
        editingId = device.Id;
        editorOpen = true;
        editorKey++;
    }

    private void CloseEditor()
    {
        editorOpen = false;
        editingId = null;
    }

    /// <summary>Dopo il salvataggio ricarica l'elenco e lascia aperto il dispositivo salvato (anche se appena creato).</summary>
    private async Task SavedAsync(Guid id)
    {
        await LoadAsync();
        editingId = id;
    }

    private Task DeleteAsync(DeviceDto device) => DeleteCoreAsync(device.Id, purgeEvents: false);

    /// <summary>Eliminazione forzata dall'avviso: cancella anche gli eventi del dispositivo.</summary>
    private Task PurgeAsync(Guid deviceId) => DeleteCoreAsync(deviceId, purgeEvents: true);

    private static string EventsText(int count) => count == 1 ? "1 evento" : $"{count} eventi";

    private async Task DeleteCoreAsync(Guid deviceId, bool purgeEvents)
    {
        deletingId = deviceId;
        deleteResult = null;
        try
        {
            deleteResult = await Api.DeleteDeviceAsync(deviceId, cts.Token, purgeEvents);
            if (deleteResult is null)
            {
                if (editingId == deviceId) CloseEditor();
                if (chartsDeviceId == deviceId) chartsDeviceId = null;
            }
            await LoadAsync();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "Eliminazione del dispositivo {DeviceId} fallita", deviceId);
            deleteResult = new DeviceInUseDto(deviceId, ex.Message, [], 0);
        }
        finally
        {
            deletingId = null;
        }
    }

    /// <summary>Descrizione delle soglie specifiche del device (tooltip del badge), null se usa quelle generali.</summary>
    private static string? CustomThresholds(DeviceDto d)
    {
        var parts = new List<string>(3);
        if (d.DownAfterFailures is { } down) parts.Add($"Down dopo {down} ping persi");
        if (d.UpAfterSuccesses is { } up) parts.Add($"Up dopo {up} ping riusciti");
        if (d.SnmpDegradedAfterFailures is { } snmp) parts.Add($"Partial dopo {snmp} errori SNMP");
        if (d.RttThresholdMs is { } rtt) parts.Add(rtt == 0 ? "latenza non controllata" : $"latenza max {rtt} ms");
        if (d.LossThresholdPct is { } loss) parts.Add(loss == 0 ? "perdita non controllata" : $"perdita max {loss:0.#} %");
        return parts.Count == 0 ? null : "Soglie specifiche: " + string.Join(", ", parts);
    }

    private string CustomerName(DeviceDto d) =>
        d.CustomerId is { } id ? customers.FirstOrDefault(c => c.Id == id)?.Name ?? "?" : "";

    private string ParentName(DeviceDto d) =>
        d.ParentDeviceId is { } p ? rows?.FirstOrDefault(r => r.Device.Id == p)?.Device.Name ?? "?" : "";

    private static string StateClass(NodeState s) => s.ToString().ToLowerInvariant();

    private static string StateTitle(DeviceOverviewDto r)
    {
        if (!r.Device.Enabled) return "Disabilitato: non monitorato";
        if (r.LastReportAt is null) return "Nessun dato dall'agente";
        var title = $"Dal {r.Since?.ToLocalTime():dd/MM/yyyy HH:mm}, ultimo report {r.LastReportAt.Value.ToLocalTime():dd/MM HH:mm:ss}";
        return r.AgentId is null ? title : $"{title} (agente {r.AgentId})";
    }

    private static string SnmpText(DeviceOverviewDto r) => r.Device.SnmpVersion switch
    {
        SnmpVersion.None => "-",
        var v => r.SnmpOk switch
        {
            true => $"{v} ok",
            false => $"{v} ko",
            null => v.ToString()
        }
    };

    private static string RttText(DeviceOverviewDto r) =>
        r.LastRttMs is { } ms && r.State != NodeState.Unknown
            ? ms.ToString(ms < 10 ? "0.0" : "0", CultureInfo.CurrentCulture) + " ms"
            : "";

    public void Dispose()
    {
        cts.Cancel();
        cts.Dispose();
    }
}
