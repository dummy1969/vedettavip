// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using VedettaVip.Shared.Contracts;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Pages;

/// <summary>
/// Pagina della Discovery: link mancanti e dispositivi nuovi proposti dai vicini letti dagli agenti; si conferma
/// scegliendo (e correggendo) le proposte. "Scopri ora" chiede una lettura immediata e aggiorna la pagina per un minuto.
/// </summary>
public partial class Discovery : IDisposable
{
    [Inject] private VedettaVipApiClient Api { get; set; } = default!;
    [Inject] private CurrentUser User { get; set; } = default!;

    /// <summary>Valori modificabili di un dispositivo proposto (stringhe per i select: Blazor non aggancia bene i Guid?).</summary>
    private sealed class DeviceRow
    {
        public bool Selected { get; set; }
        public string Name { get; set; } = "";
        public string Address { get; set; } = "";
        public DeviceType Type { get; set; }
        public bool RouterOs { get; set; }
        public string CustomerId { get; set; } = "";
        public string MapId { get; set; } = "";
    }

    private readonly CancellationTokenSource cts = new();
    private DiscoveryResultDto? result;
    private readonly Dictionary<string, DeviceRow> rows = [];
    private readonly HashSet<string> selectedLinks = [];
    private IReadOnlyList<CustomerDto> customers = [];
    private IReadOnlyList<MapSummaryDto> maps = [];
    private DiscoveryAcceptResultDto? accepted;
    private IReadOnlyList<DiscoveryScanDto> scans = [];
    private string scanCidr = "";
    private string scanCustomerId = "";
    private string scanMapId = "";
    private string sourceFilter = "";
    /// <summary>"dev:{Id}" = visto da quel router, "scan:{Id}" = trovato da quella scansione.</summary>
    private string seenByFilter = "";
    private string customerFilter = "";
    private const string NoCustomer = "none";
    private DiscoveryForgetResultDto? forgotten;
    private bool showAll;
    private bool pollingScans;

    /// <summary>Righe mostrate senza "mostra tutti": l'ARP di molti router può portare centinaia di host.</summary>
    private const int MaxRows = 200;
    private string filter = "";
    private bool busy;
    private bool refreshing;
    private string? error;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            await User.GetAsync();
            customers = await Api.GetCustomersAsync(cts.Token);
            maps = await Api.GetMapsAsync(cts.Token);
        }
        catch (HttpRequestException ex)
        {
            error = ex.Message;
        }
        await LoadAsync();
        await LoadScansAsync();
    }

    private async Task LoadScansAsync()
    {
        try
        {
            scans = await Api.GetScansAsync(cts.Token);
            if (!pollingScans && scans.Any(Active))
                _ = PollScansAsync();
        }
        catch (HttpRequestException ex)
        {
            error = $"Scansioni non caricate: {ex.Message}";
        }
    }

    private static bool Active(DiscoveryScanDto s) => s.Status is DiscoveryScanStatus.Pending or DiscoveryScanStatus.Running;

    /// <summary>Avanzamento ogni 3 s finché ci sono scansioni in corso; alla fine si ricaricano le proposte.</summary>
    private async Task PollScansAsync()
    {
        pollingScans = true;
        try
        {
            while (!cts.IsCancellationRequested && scans.Any(Active))
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cts.Token);
                scans = await Api.GetScansAsync(cts.Token);
                if (!scans.Any(Active))
                    await LoadAsync();
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) { }
        catch (HttpRequestException ex)
        {
            error = ex.Message;
        }
        finally
        {
            pollingScans = false;
        }
    }

    private async Task StartScanAsync() => await RunAsync(async () =>
    {
        await Api.CreateScanAsync(new DiscoveryScanCreateDto(scanCidr.Trim(),
            Guid.TryParse(scanCustomerId, out var c) ? c : null, Guid.TryParse(scanMapId, out var m) ? m : null), cts.Token);
        scanCidr = "";
        await LoadScansAsync();
    });

    private async Task DeleteScanAsync(Guid id) => await RunAsync(async () =>
    {
        await Api.DeleteScanAsync(id, cts.Token);
        await LoadScansAsync();
        await LoadAsync();
    });

    private static string ScanStatus(DiscoveryScanDto s) => s.Status switch
    {
        DiscoveryScanStatus.Pending => "in attesa dell'agente",
        DiscoveryScanStatus.Running => s.Total > 0 ? $"in corso: {s.Scanned} di {s.Total}" : "in corso",
        DiscoveryScanStatus.Done => $"{s.HostsAlive} host vivi su {s.Total}",
        _ => $"fallita: {s.Error}"
    };

    private void SelectVisible(bool on)
    {
        foreach (var d in Devices().Take(showAll ? int.MaxValue : MaxRows).Where(d => d.Address is not null || !on))
            rows[d.Key].Selected = on;
    }

    private async Task LoadAsync()
    {
        try
        {
            result = await Api.GetDiscoveryAsync(cts.Token);
            // Le righe già presenti mantengono le modifiche; le nuove partono dai suggerimenti
            foreach (var d in result.Devices)
                rows.TryAdd(d.Key, new DeviceRow
                {
                    Name = d.Name, Address = d.Address ?? "", Type = d.Type, RouterOs = d.RouterOs,
                    CustomerId = d.SuggestedCustomerId?.ToString() ?? "", MapId = d.SuggestedMapId?.ToString() ?? ""
                });
            selectedLinks.IntersectWith(result.Links.Where(l => l.Problem is null).Select(l => l.Key));
            error = null;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (HttpRequestException ex)
        {
            error = $"Proposte non caricate: {ex.Message}";
        }
    }

    private bool Matches(params string?[] values)
    {
        var text = filter.Trim();
        return text.Length == 0 || values.Any(v => v?.Contains(text, StringComparison.OrdinalIgnoreCase) == true);
    }

    private IEnumerable<DiscoveryLinkProposalDto> Links() => result?.Links
        .Where(l => Matches(l.FromName, l.ToName, l.FromInterface, l.ToInterface, l.MapName)) ?? [];

    private IEnumerable<DiscoveryDeviceProposalDto> Devices() => result?.Devices
        .Where(d => sourceFilter.Length == 0 || d.Sources.Split(',').Contains(sourceFilter))
        .Where(d => seenByFilter.Length == 0 || seenByFilter == $"scan:{d.ScanId}" || d.SeenBy.Any(s => seenByFilter == $"dev:{s.DeviceId}"))
        .Where(d => customerFilter.Length == 0 || (rows.GetValueOrDefault(d.Key)?.CustomerId is { } c && (c == customerFilter || c.Length == 0 && customerFilter == NoCustomer)))
        .Where(d => Matches(d.Name, d.Address, d.Platform, d.Board, d.MacAddress, d.Description, d.Vendor) || d.SeenBy.Any(s => Matches(s.DeviceName, s.LocalInterface))) ?? [];

    /// <summary>Router che hanno visto qualcosa e scansioni con risultati, per il filtro "visto da".</summary>
    private IEnumerable<(string Value, string Label)> SeenByOptions() =>
        (result?.Devices.SelectMany(d => d.SeenBy).DistinctBy(s => s.DeviceId)
            .OrderBy(s => s.DeviceName, StringComparer.CurrentCultureIgnoreCase)
            .Select(s => ($"dev:{s.DeviceId}", $"visto da: {s.DeviceName}")) ?? [])
        .Concat(scans.Where(s => result?.Devices.Any(d => d.ScanId == s.Id) == true)
            .Select(s => ($"scan:{s.Id}", $"scansione {s.Cidr} del {s.CreatedAt.ToLocalTime():dd/MM HH:mm}")));

    private List<string> SelectedDevices() =>
        rows.Where(r => r.Value.Selected && result?.Devices.Any(d => d.Key == r.Key) == true).Select(r => r.Key).ToList();

    /// <summary>Elimina le letture dei dispositivi selezionati (i link proposti non hanno letture proprie: si ignorano).</summary>
    private async Task ForgetSelectedAsync() => await RunAsync(async () =>
    {
        var keys = SelectedDevices();
        var total = new DiscoveryForgetResultDto(0, 0, 0);
        foreach (var chunk in keys.Chunk(1000))
        {
            var r = await Api.ForgetDiscoveryAsync(new DiscoveryForgetDto(chunk), cts.Token);
            total = new(total.ArpEntries + r.ArpEntries, total.Neighbors + r.Neighbors, total.ScanHosts + r.ScanHosts);
        }
        forgotten = total;
        foreach (var key in keys)
            rows.Remove(key);
        await LoadAsync();
    });

    private int SelectedCount => selectedLinks.Count + rows.Count(r => r.Value.Selected && result?.Devices.Any(d => d.Key == r.Key) == true);

    private string SelectedText()
    {
        var devices = rows.Count(r => r.Value.Selected && result?.Devices.Any(d => d.Key == r.Key) == true);
        return $"{devices} dispositivi, {selectedLinks.Count} link";
    }

    private static void Toggle(HashSet<string> set, string key, bool on)
    {
        if (on) set.Add(key); else set.Remove(key);
    }

    private static string SourceText(DiscoveryLinkProposalDto l) =>
        l.SourceDeviceId is null ? "nessuna sorgente (interfaccia non nell'inventario SNMP)"
            : $"{(l.SourceDeviceId == l.FromDeviceId ? l.FromName : l.ToName)}, IfIndex {l.SourceIfIndex} · {l.SpeedBps / 1_000_000:N0} Mbit/s";

    private static string Details(DiscoveryDeviceProposalDto d) =>
        string.Join(" · ", new[]
        {
            d.Vendor, d.Board ?? d.Platform, d.Version, d.Description != d.Platform ? d.Description : null,
            d.OpenPorts is { Count: > 0 } p ? "porte " + string.Join(',', p) : null, d.Problem
        }.Where(x => !string.IsNullOrEmpty(x)));

    private RenderFragment IgnoreButton(string key) => builder =>
    {
        if (!User.CanEdit) return;
        builder.OpenElement(0, "button");
        builder.AddAttribute(1, "class", "btn btn-sm btn-link text-muted p-0");
        builder.AddAttribute(2, "title", "Non proporlo più");
        builder.AddAttribute(3, "disabled", busy);
        builder.AddAttribute(4, "onclick", EventCallback.Factory.Create(this, () => IgnoreAsync(key)));
        builder.AddContent(5, "Ignora");
        builder.CloseElement();
    };

    private async Task IgnoreAsync(string key) => await RunAsync(async () =>
    {
        await Api.IgnoreDiscoveryAsync(new DiscoveryIgnoreDto([key]), cts.Token);
        selectedLinks.Remove(key);
        rows.Remove(key);
        await LoadAsync();
    });

    /// <summary>Ignora in un colpo link e dispositivi selezionati (con "seleziona i visibili" = tutti quelli filtrati).</summary>
    private async Task IgnoreSelectedAsync() => await RunAsync(async () =>
    {
        var keys = selectedLinks
            .Concat(rows.Where(r => r.Value.Selected && result?.Devices.Any(d => d.Key == r.Key) == true).Select(r => r.Key))
            .ToList();
        foreach (var chunk in keys.Chunk(1000)) // limite della richiesta
            await Api.IgnoreDiscoveryAsync(new DiscoveryIgnoreDto(chunk), cts.Token);
        selectedLinks.Clear();
        foreach (var key in keys)
            rows.Remove(key);
        await LoadAsync();
    });

    private async Task RestoreIgnoredAsync() => await RunAsync(async () =>
    {
        await Api.IgnoreDiscoveryAsync(new DiscoveryIgnoreDto([], Ignore: false), cts.Token);
        await LoadAsync();
    });

    private async Task AcceptAsync() => await RunAsync(async () =>
    {
        var devices = result!.Devices.Where(d => rows[d.Key].Selected).Select(d =>
        {
            var r = rows[d.Key];
            return new DiscoveryDeviceAcceptDto(d.Key, r.Name.Trim(), r.Address.Trim(), r.Type, r.RouterOs,
                Guid.TryParse(r.CustomerId, out var c) ? c : null, Guid.TryParse(r.MapId, out var m) ? m : null, d.SuggestedParentId,
                d.Snmp, d.SnmpProfileId);
        }).ToList();
        if (devices.FirstOrDefault(d => d.Name.Length == 0 || d.Address.Length == 0) is { } incomplete)
        {
            error = $"Nome e indirizzo obbligatori (\"{incomplete.Name}\").";
            return;
        }
        accepted = await Api.AcceptDiscoveryAsync(new DiscoveryAcceptDto(devices, [.. selectedLinks]), cts.Token);
        selectedLinks.Clear();
        foreach (var d in devices)
            rows.Remove(d.Key);
        maps = await Api.GetMapsAsync(cts.Token);
        await LoadAsync();
    });

    /// <summary>"Scopri ora": gli agenti rileggono i vicini in pochi secondi; la pagina si aggiorna per un minuto.</summary>
    private async Task RefreshAsync()
    {
        refreshing = true;
        try
        {
            await Api.RefreshDiscoveryAsync(cts.Token);
            for (var i = 0; i < 6 && !cts.IsCancellationRequested; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cts.Token);
                await LoadAsync();
                StateHasChanged();
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (HttpRequestException ex)
        {
            error = ex.Message;
        }
        finally
        {
            refreshing = false;
        }
    }

    private async Task RunAsync(Func<Task> action)
    {
        busy = true;
        error = null;
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        catch (HttpRequestException ex)
        {
            error = ex.Message;
        }
        finally
        {
            busy = false;
        }
    }

    public void Dispose()
    {
        cts.Cancel();
        cts.Dispose();
    }
}
