// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Components;

/// <summary>
/// Pannello della modalità modifica: aggiunta di nodi nel punto scelto, proprietà del nodo o del link
/// selezionato. Chiama l'API direttamente e notifica la pagina con i dati restituiti dal server.
/// </summary>
public partial class MapEditPanel : IDisposable
{
    private const string NewMapOption = "new";
    private static readonly long[] SpeedPresetsMbps = [100, 1_000, 2_500, 10_000, 25_000, 40_000, 100_000];

    [Inject] private VedettaVipApiClient Api { get; set; } = default!;
    [Inject] private ILogger<MapEditPanel> Logger { get; set; } = default!;

    [Parameter, EditorRequired] public Guid MapId { get; set; }
    [Parameter, EditorRequired] public List<MapNode> Nodes { get; set; } = [];
    [Parameter] public MapNode? SelectedNode { get; set; }
    [Parameter] public MapLink? SelectedLink { get; set; }
    [Parameter] public MapPoint? PlacePoint { get; set; }

    [Parameter] public EventCallback OnPlaceAtCenter { get; set; }
    [Parameter] public EventCallback OnPlaceCancel { get; set; }
    [Parameter] public EventCallback<MapNodeDto> OnNodeSaved { get; set; }
    [Parameter] public EventCallback<Guid> OnNodeDeleted { get; set; }
    [Parameter] public EventCallback<MapLinkDto> OnLinkSaved { get; set; }
    [Parameter] public EventCallback<Guid> OnLinkDeleted { get; set; }
    /// <summary>Apre il grafico del traffico del link sotto la mappa.</summary>
    [Parameter] public EventCallback<MapLink> OnShowChart { get; set; }

    private readonly CancellationTokenSource cts = new();
    private List<DeviceDto> devices = [];
    private List<MapSummaryDto> maps = [];
    private Guid? loadedForMap;
    private Guid? editedId;
    private bool busy;
    private string? error;

    // Aggiunta nodo
    private MapNodeKind addKind = MapNodeKind.Device;
    private string addDeviceId = "";
    private string addSubmapId = NewMapOption;
    private string addName = "";

    // Nodo selezionato
    private string labelTemplate = "";
    private string? nodeIcon;

    // Link selezionato
    private long linkSpeedMbps;
    private string linkDeviceId = "";
    private int? linkIfIndex;
    private int? linkUtilThreshold;
    private MetricThresholdsDto? metricThresholds;

    // Interfacce del device sorgente del traffico (inventario SNMP dell'agente)
    private DeviceInterfacesDto? interfaces;
    private bool refreshingInterfaces;

    protected override async Task OnParametersSetAsync()
    {
        var selectedId = SelectedNode?.Id ?? SelectedLink?.Id;
        if (selectedId != editedId)
        {
            editedId = selectedId;
            error = null;
            ResetEditFields();
            if (SelectedLink is not null)
                await LoadInterfacesAsync();
        }

        if (loadedForMap != MapId)
        {
            loadedForMap = MapId;
            await LoadListsAsync();
        }
    }

    private void ResetEditFields()
    {
        if (SelectedNode is { } node)
        {
            labelTemplate = node.LabelTemplate;
            nodeIcon = node.OwnIcon;
        }

        if (SelectedLink is { } link)
        {
            linkSpeedMbps = Math.Max(1, link.SpeedBps / 1_000_000);
            linkDeviceId = link.DeviceId?.ToString() ?? "";
            linkIfIndex = link.IfIndex;
            linkUtilThreshold = link.UtilizationThresholdPct;
        }
    }

    private async Task OnLinkDeviceChangedAsync()
    {
        linkIfIndex = null; // l'IfIndex appartiene al device precedente
        await LoadInterfacesAsync();
    }

    /// <summary>
    /// Carica le interfacce del device scelto. Se l'elenco è vuoto e il device è in SNMP v2c, chiede una volta
    /// un inventario immediato all'agente (device appena creato o mai letto).
    /// </summary>
    private async Task LoadInterfacesAsync()
    {
        interfaces = null;
        if (!Guid.TryParse(linkDeviceId, out var deviceId))
            return;

        try
        {
            interfaces = await Api.GetDeviceInterfacesAsync(deviceId, cts.Token);
            if (interfaces.Interfaces.Count == 0 && devices.Find(d => d.Id == deviceId)?.SnmpVersion == SnmpVersion.V2c)
                await RefreshInterfacesAsync();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning("Interfacce del device {DeviceId} non caricate: {Message}", deviceId, ex.Message);
        }
    }

    /// <summary>Chiede all'agente un inventario e attende il risultato (fino a ~15 s, controllando ogni 1,5 s).</summary>
    private async Task RefreshInterfacesAsync()
    {
        if (!Guid.TryParse(linkDeviceId, out var deviceId) || refreshingInterfaces)
            return;

        refreshingInterfaces = true;
        StateHasChanged();
        try
        {
            var before = interfaces?.UpdatedAt;
            await Api.RefreshDeviceInterfacesAsync(deviceId, cts.Token);
            for (var attempt = 0; attempt < 10; attempt++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(1500), cts.Token);
                var latest = await Api.GetDeviceInterfacesAsync(deviceId, cts.Token);
                if (latest.UpdatedAt != before)
                {
                    // Solo se nel frattempo non è stato scelto un altro device
                    if (linkDeviceId == deviceId.ToString())
                        interfaces = latest;
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning("Aggiornamento interfacce di {DeviceId} fallito: {Message}", deviceId, ex.Message);
        }
        finally
        {
            refreshingInterfaces = false;
        }
    }

    private string InterfacesStatus()
    {
        var device = Guid.TryParse(linkDeviceId, out var id) ? devices.Find(d => d.Id == id) : null;
        if (interfaces is { UpdatedAt: { } at, Interfaces.Count: > 0 })
            return $"{interfaces.Interfaces.Count} interfacce, lette alle {at.ToLocalTime():HH:mm}.";
        if (refreshingInterfaces)
            return "Lettura delle interfacce dal dispositivo...";
        return device?.SnmpVersion == SnmpVersion.V2c
            ? "Elenco non disponibile (dispositivo non raggiungibile via SNMP?): inserire l'IfIndex."
            : "Elenco interfacce disponibile solo con SNMP v2c: inserire l'IfIndex.";
    }

    /// <summary>Es. "2 — ether1Wan · WAN fibra (2.5G)" e "[down]" se l'interfaccia è giù.</summary>
    private static string InterfaceText(DeviceInterfaceDto i)
    {
        var text = $"{i.IfIndex} — {i.Name ?? "?"}";
        if (i.Alias is { Length: > 0 } alias && alias != i.Name)
            text += $" · {alias}";
        if (i.SpeedBps is { } speed)
            text += $" ({NetworkMap.Bps(speed)})";
        if (i.OperStatus == InterfaceOperStatus.Down)
            text += " [down]";
        return text;
    }

    private async Task LoadListsAsync()
    {
        try
        {
            var devicesTask = Api.GetDevicesAsync(cts.Token);
            var mapsTask = Api.GetMapsAsync(cts.Token);
            devices = [.. await devicesTask];
            maps = [.. await mapsTask];
            metricThresholds = await Api.GetMetricThresholdsAsync(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "Caricamento di dispositivi e mappe fallito");
            error = $"Impossibile caricare dispositivi e mappe: {ex.Message}";
        }
    }

    private MapNode? FindNode(Guid id) => Nodes.Find(n => n.Id == id);

    private IEnumerable<DeviceDto> AvailableDevices()
    {
        var onMap = Nodes.Where(n => n.DeviceId is not null).Select(n => n.DeviceId!.Value).ToHashSet();
        return devices.Where(d => !onMap.Contains(d.Id));
    }

    /// <summary>Mappe agganciabili come sottomappa: radici, diverse da questa mappa e dai suoi antenati.</summary>
    private IEnumerable<MapSummaryDto> AvailableSubmaps()
    {
        var excluded = new HashSet<Guid>();
        Guid? current = MapId;
        while (current is { } id && excluded.Add(id))
            current = maps.Find(m => m.Id == id)?.ParentMapId;

        return maps.Where(m => m.ParentMapId is null && !excluded.Contains(m.Id));
    }

    /// <summary>Dispositivi proposti come sorgente del traffico: quelli dei due estremi, più quello già impostato.</summary>
    private IEnumerable<(string Id, string Name)> LinkDeviceOptions(MapLink link)
    {
        var ids = new List<Guid>();
        foreach (var n in new[] { FindNode(link.FromId), FindNode(link.ToId) })
            if (n?.DeviceId is { } id && !ids.Contains(id))
                ids.Add(id);
        if (link.DeviceId is { } current && !ids.Contains(current))
            ids.Add(current);

        return ids.Select(id => (id.ToString(), devices.Find(d => d.Id == id)?.Name ?? id.ToString()));
    }

    private bool CanAdd() => addKind switch
    {
        MapNodeKind.Device => Guid.TryParse(addDeviceId, out _),
        MapNodeKind.Submap => addSubmapId != NewMapOption || !string.IsNullOrWhiteSpace(addName),
        _ => !string.IsNullOrWhiteSpace(addName)
    };

    private async Task AddNodeAsync()
    {
        if (PlacePoint is not { } p || !CanAdd()) return;

        await RunAsync(async ct =>
        {
            CreateMapNodeDto dto;
            switch (addKind)
            {
                case MapNodeKind.Device:
                    dto = new(MapNodeKind.Device, Guid.Parse(addDeviceId), null, p.X, p.Y, null, null);
                    break;

                case MapNodeKind.Submap:
                    var submapId = addSubmapId == NewMapOption
                        ? (await Api.CreateMapAsync(new MapUpsertDto(addName.Trim()), ct)).Id
                        : Guid.Parse(addSubmapId);
                    dto = new(MapNodeKind.Submap, null, submapId, p.X, p.Y, null, null);
                    break;

                default:
                    dto = new(MapNodeKind.Static, null, null, p.X, p.Y, addName.Trim(), null);
                    break;
            }

            var created = await Api.CreateNodeAsync(MapId, dto, ct);
            addDeviceId = "";
            addName = "";
            addSubmapId = NewMapOption;
            await OnNodeSaved.InvokeAsync(created);

            // Le sottomappe agganciate (o appena create) cambiano l'elenco delle mappe disponibili
            if (addKind == MapNodeKind.Submap)
                await LoadListsAsync();
        });
    }

    private async Task SaveNodeAsync()
    {
        if (SelectedNode is not { } node) return;

        await RunAsync(async ct =>
        {
            var updated = await Api.UpdateNodeAsync(MapId, node.Id, new UpdateMapNodeDto(labelTemplate, nodeIcon), ct);
            labelTemplate = updated.LabelTemplate;
            await OnNodeSaved.InvokeAsync(updated);
        });
    }

    /// <summary>Icona usata se il nodo non ne sceglie una: quella del dispositivo o la predefinita del tipo.</summary>
    private static (string? Key, string Source) InheritedIcon(MapNode node) =>
        node.OwnIcon is null && node.Icon is { } effective
            ? (effective, effective == MapIcons.DefaultFor(node.Kind, node.DeviceType) ? " del tipo" : " (dal dispositivo)")
            : (MapIcons.DefaultFor(node.Kind, node.DeviceType), " del tipo");

    private async Task DeleteNodeAsync()
    {
        if (SelectedNode is not { } node) return;

        await RunAsync(async ct =>
        {
            await Api.DeleteNodeAsync(MapId, node.Id, ct);
            await OnNodeDeleted.InvokeAsync(node.Id);
            if (node.Kind == MapNodeKind.Submap)
                await LoadListsAsync();
        });
    }

    private async Task SaveLinkAsync(bool swap)
    {
        if (SelectedLink is not { } link) return;

        if (linkSpeedMbps < 1)
        {
            error = "La velocità deve essere almeno 1 Mbps.";
            return;
        }

        await RunAsync(async ct =>
        {
            Guid? deviceId = Guid.TryParse(linkDeviceId, out var d) ? d : null;
            var (from, to) = swap ? (link.ToId, link.FromId) : (link.FromId, link.ToId);
            var dto = new MapLinkUpsertDto(from, to, deviceId, deviceId is null ? null : linkIfIndex, linkSpeedMbps * 1_000_000, linkUtilThreshold);

            await Api.UpdateLinkAsync(MapId, link.Id, dto, ct);
            await OnLinkSaved.InvokeAsync(new MapLinkDto(link.Id, from, to, dto.DeviceId, dto.IfIndex, dto.SpeedBps, dto.UtilizationThresholdPct));
        });
    }

    private async Task DeleteLinkAsync()
    {
        if (SelectedLink is not { } link) return;

        await RunAsync(async ct =>
        {
            await Api.DeleteLinkAsync(MapId, link.Id, ct);
            await OnLinkDeleted.InvokeAsync(link.Id);
        });
    }

    /// <summary>Esegue un'operazione verso l'API mostrando nel pannello l'eventuale errore.</summary>
    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        busy = true;
        error = null;
        try
        {
            await action(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "Operazione di modifica della mappa fallita");
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

    private static string KindLabel(MapNodeKind kind) => kind switch
    {
        MapNodeKind.Device => "Dispositivo",
        MapNodeKind.Submap => "Sottomappa",
        _ => "Nodo statico"
    };

    private static string NodeName(MapNode? node) =>
        node is null ? "?"
        : node.Values.TryGetValue("Name", out var name) && !string.IsNullOrEmpty(name) ? name
        : node.LabelTemplate.Split('\n')[0];

    private static string SpeedText(long mbps) =>
        mbps >= 1_000 ? (mbps / 1_000d).ToString("0.#", CultureInfo.InvariantCulture) + "G" : mbps + "M";
}
