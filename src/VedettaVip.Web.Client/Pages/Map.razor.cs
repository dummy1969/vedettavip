// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Microsoft.Extensions.Logging;
using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;
using VedettaVip.Web.Client.Components;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Pages;

public partial class Map : IAsyncDisposable
{
    private static readonly TimeSpan AgentCheckInterval = TimeSpan.FromSeconds(60);

    [Inject] private VedettaVipApiClient Api { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private ILogger<Map> Logger { get; set; } = default!;
    [Inject] private CurrentUser User { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>Chiave del localStorage con l'ultima mappa aperta su questo browser.</summary>
    private const string LastMapKey = "vedettavip.lastMap";

    // Tutte le mappe (selettore ad albero) e gestione delle mappe in modalità modifica
    private IReadOnlyList<MapSummaryDto> allMaps = [];
    private enum MapAction { None, New, Rename }
    private MapAction mapAction;
    private string mapNameInput = "";
    private bool mapBusy;

    /// <summary>Mappa da aprire; se assente si apre la prima mappa radice.</summary>
    [Parameter] public Guid? MapId { get; set; }

    private readonly CancellationTokenSource cts = new();
    private List<MapNode> nodes = [];
    private List<MapLink> links = [];
    private MapDto? map;
    private string? parentName;
    private string? loadError;
    private string? saveError;
    private string? agentWarning;
    private Guid? loadedMapId;
    private bool backgroundStarted;
    private HubConnection? hub;
    private bool liveConnected;

    // Modalità modifica (default: sola visualizzazione, adatta ai monitor NOC)
    private NetworkMap? mapView;
    private bool editMode;
    private MapTool tool = MapTool.Move;
    private Guid? selectedId;
    private MapPoint? placePoint;

    // Ultimo campione per interfaccia (DeviceId, IfIndex), da GET /api/traffic e dal messaggio "TrafficUpdated"
    private readonly Dictionary<(Guid DeviceId, int IfIndex), InterfaceTrafficDto> traffic = [];

    // Link di cui mostrare il grafico (per Id: il modello del link viene sostituito quando lo si salva)
    private Guid? chartLinkId;
    private MapLink? ChartLink => chartLinkId is { } id ? links.Find(l => l.Id == id) : null;
    // Device di cui mostrare latenza e traffico (doppio clic sul nodo); esclusivo con il grafico del link
    private Guid? chartNodeId;
    private MapNode? ChartNode => chartNodeId is { } id ? nodes.Find(n => n.Id == id) : null;

    private MapNode? SelectedNode => selectedId is { } id ? nodes.Find(n => n.Id == id) : null;
    private MapLink? SelectedLink => selectedId is { } id ? links.Find(l => l.Id == id) : null;

    protected override async Task OnParametersSetAsync()
    {
        try
        {
            await User.GetAsync(); // ruolo: il bottone Modifica è solo per Admin e Operatori
        }
        catch (HttpRequestException)
        {
            // API non raggiungibile: lo mostra già il caricamento della mappa
        }

        // Navigando tra /map/{a} e /map/{b} Blazor riusa la stessa istanza: ricarica solo se cambia la mappa
        if (MapId is null || MapId != loadedMapId)
            await LoadAsync();

        if (!backgroundStarted)
        {
            backgroundStarted = true;
            _ = RefreshTrafficAgeAsync(cts.Token);
            _ = MonitorAgentsAsync(cts.Token);
            _ = ConnectLiveAsync(cts.Token);
        }
    }

    private async Task ReloadAsync()
    {
        loadedMapId = null;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        loadError = null;
        saveError = null;
        map = null;
        selectedId = null;
        placePoint = null;
        chartLinkId = null;
        chartNodeId = null;

        try
        {
            allMaps = await Api.GetMapsAsync(cts.Token);
            var mapId = MapId ?? await InitialMapAsync();
            if (mapId is null)
            {
                loadError = "Nessuna mappa: creane una con \"+ Nuova mappa\" in alto a destra.";
                return;
            }

            var dto = await Api.GetMapAsync(mapId.Value, cts.Token);
            if (dto is null)
            {
                loadError = $"La mappa {mapId} non esiste.";
                return;
            }

            parentName = dto.ParentMapId is { } parentId ? allMaps.FirstOrDefault(m => m.Id == parentId)?.Name : null;
            await RememberMapAsync(dto.Id);

            nodes = dto.Nodes.Select(ToModel).ToList();
            links = dto.Links.Select(ToModel).ToList();
            map = dto;
            loadedMapId = MapId;

            await LoadTrafficAsync();
            await LoadRouterOsAsync();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "Errore nel caricamento della mappa {MapId}", MapId);
            loadError = $"API non raggiungibile o in errore ({ex.StatusCode?.ToString() ?? ex.Message}).";
        }
    }

    private static MapNode ToModel(MapNodeDto dto)
    {
        var node = new MapNode
        {
            Id = dto.Id,
            Kind = dto.Kind,
            DeviceId = dto.DeviceId,
            SubmapId = dto.SubmapId,
            X = dto.X,
            Y = dto.Y,
            LabelTemplate = dto.LabelTemplate,
            State = dto.State,
            Maintenance = dto.Maintenance,
            Icon = MapIcons.Resolve(dto.Icon, MapIcons.DefaultFor(dto.Kind, dto.DeviceType)),
            OwnIcon = dto.OwnIcon,
            DeviceType = dto.DeviceType
        };
        node.Values["Name"] = dto.Name ?? "";
        node.Values["Address"] = dto.Address ?? "";
        if (dto.Kind == MapNodeKind.Device)
            RouterOsText.Apply(node.Values, null);
        foreach (var m in dto.SubmapMembers ?? [])
            node.SubmapMembers[m.DeviceId] = m;
        node.RefreshSubmapState();
        return node;
    }

    private static MapLink ToModel(MapLinkDto dto) => new()
    {
        Id = dto.Id,
        FromId = dto.FromNodeId,
        ToId = dto.ToNodeId,
        DeviceId = dto.DeviceId,
        IfIndex = dto.IfIndex,
        SpeedBps = dto.SpeedBps,
        UtilizationThresholdPct = dto.UtilizationThresholdPct
    };

    /// <summary>
    /// Mappa da aprire su /map: l'ultima usata su questo browser se esiste ancora, altrimenti la mappa principale
    /// con più nodi (a parità, per nome). Null se non ci sono mappe.
    /// </summary>
    private async Task<Guid?> InitialMapAsync()
    {
        try
        {
            if (await JS.InvokeAsync<string?>("localStorage.getItem", LastMapKey) is { } stored
                && Guid.TryParse(stored, out var last) && allMaps.Any(m => m.Id == last))
                return last;
        }
        catch (JSException)
        {
            // localStorage non disponibile (es. navigazione privata con restrizioni): si usa il criterio predefinito
        }

        return allMaps.Where(m => m.ParentMapId is null)
                   .OrderByDescending(m => m.NodeCount).ThenBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
                   .FirstOrDefault()?.Id
               ?? allMaps.FirstOrDefault()?.Id;
    }

    private async Task RememberMapAsync(Guid id)
    {
        try
        {
            await JS.InvokeVoidAsync("localStorage.setItem", LastMapKey, id.ToString());
        }
        catch (JSException) { }
    }

    private IEnumerable<(MapSummaryDto Map, int Depth)> MapTree() => MapHierarchy.Order(allMaps);

    private static string Indent(int depth) => MapHierarchy.Indent(depth);

    private void OnMapSelected(ChangeEventArgs e)
    {
        if (Guid.TryParse(e.Value?.ToString(), out var id) && id != map?.Id)
            Navigation.NavigateTo($"map/{id}");
    }

    private void StartMapAction(MapAction action)
    {
        mapAction = action;
        mapNameInput = action == MapAction.Rename ? map?.Name ?? "" : "";
        saveError = null;
    }

    /// <summary>Nuova mappa principale (poi la si apre) o rinomina della mappa corrente.</summary>
    private async Task SaveMapActionAsync()
    {
        var name = mapNameInput.Trim();
        if (name.Length == 0 || map is null && mapAction == MapAction.Rename)
            return;

        mapBusy = true;
        try
        {
            if (mapAction == MapAction.New)
            {
                var created = await Api.CreateMapAsync(new MapUpsertDto(name), cts.Token);
                mapAction = MapAction.None;
                Navigation.NavigateTo($"map/{created.Id}");
            }
            else
            {
                await Api.UpdateMapAsync(map!.Id, new MapUpsertDto(name, map.BackgroundImage, map.GridSize), cts.Token);
                mapAction = MapAction.None;
                await ReloadAsync();
            }
        }
        catch (HttpRequestException ex)
        {
            saveError = ex.Message;
        }
        finally
        {
            mapBusy = false;
        }
    }

    /// <summary>Elimina la mappa corrente (nodi e link compresi) e torna alla mappa iniziale.</summary>
    private async Task DeleteCurrentMapAsync()
    {
        if (map is null) return;
        mapBusy = true;
        try
        {
            await Api.DeleteMapAsync(map.Id, cts.Token);
            editMode = false;
            loadedMapId = null;
            try { await JS.InvokeVoidAsync("localStorage.removeItem", LastMapKey); } catch (JSException) { }
            if (MapId is null)
                await ReloadAsync(); // già su /map: si ricarica la mappa iniziale
            else
                Navigation.NavigateTo("map");
        }
        catch (HttpRequestException ex)
        {
            saveError = ex.Message; // es. "è una sottomappa di …: eliminare prima il nodo che la rappresenta"
        }
        finally
        {
            mapBusy = false;
        }
    }

    /// <summary>Ultima lettura RouterOS per device (variabili [Cpu] [Mem] [Temp] [Uptime]... dei nodi).</summary>
    private readonly Dictionary<Guid, RouterOsSampleDto> routerOs = [];

    private async Task LoadRouterOsAsync()
    {
        try
        {
            routerOs.Clear();
            foreach (var sample in await Api.GetRouterOsAsync(cts.Token))
                routerOs[sample.DeviceId] = sample;
            foreach (var node in nodes)
                ApplyRouterOs(node);
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning("Letture RouterOS iniziali non caricate: {Message}", ex.Message); // arrivano con RouterOsUpdated
        }
    }

    private void ApplyRouterOs(MapNode node)
    {
        if (node.Kind == MapNodeKind.Device && node.DeviceId is { } id)
            RouterOsText.Apply(node.Values, routerOs.GetValueOrDefault(id));
    }

    private async Task LoadTrafficAsync()
    {
        try
        {
            traffic.Clear();
            foreach (var sample in await Api.GetTrafficAsync(cts.Token))
                traffic[(sample.DeviceId, sample.IfIndex)] = sample;
        }
        catch (HttpRequestException ex)
        {
            // Non blocca la mappa: i link restano "n/d" fino al prossimo TrafficUpdated
            Logger.LogWarning("Traffico iniziale non caricato: {Message}", ex.Message);
        }
        ApplyTraffic();
    }

    private void OnTrafficUpdated(IReadOnlyList<InterfaceTrafficDto> samples)
    {
        foreach (var sample in samples)
            traffic[(sample.DeviceId, sample.IfIndex)] = sample;
        ApplyTraffic();
    }

    /// <summary>
    /// Copia sui link il traffico della loro interfaccia. Direzione: tx = From → To. Se l'interfaccia è del device
    /// del nodo To, il traffico From → To entra da quell'interfaccia (tx = In); altrimenti (device del nodo From
    /// o di un terzo nodo) tx = Out.
    /// </summary>
    private void ApplyTraffic()
    {
        foreach (var link in links)
        {
            if (link is not { DeviceId: { } deviceId, IfIndex: { } ifIndex } || !traffic.TryGetValue((deviceId, ifIndex), out var s))
            {
                (link.TxBps, link.RxBps, link.TrafficTime, link.IfName, link.DetectedSpeedBps) = (0, 0, null, null, null);
                continue;
            }

            (link.TxBps, link.RxBps) = TxIsIn(link) ? (s.InBps, s.OutBps) : (s.OutBps, s.InBps);
            (link.TrafficTime, link.IfName, link.DetectedSpeedBps) = (s.Time, s.Name, s.SpeedBps);
        }
    }

    /// <summary>Il tx del link (From → To) entra dall'interfaccia misurata se questa è sul device del nodo To.</summary>
    private bool TxIsIn(MapLink link)
    {
        var fromDevice = nodes.Find(n => n.Id == link.FromId)?.DeviceId;
        var toDevice = nodes.Find(n => n.Id == link.ToId)?.DeviceId;
        return link.DeviceId is { } d && d == toDevice && d != fromDevice;
    }

    private string NodeName(Guid nodeId)
    {
        var node = nodes.Find(n => n.Id == nodeId);
        if (node is null) return "?";
        return node.Values.TryGetValue("Name", out var name) && !string.IsNullOrEmpty(name) ? name : node.LabelTemplate.Split('\n')[0];
    }

    private void OpenChart(MapLink link)
    {
        chartNodeId = null;
        chartLinkId = link.Id;
    }

    /// <summary>Stati live via SignalR. Dopo una riconnessione ricarica la mappa per recuperare i cambi persi.</summary>
    private async Task ConnectLiveAsync(CancellationToken ct)
    {
        hub = new HubConnectionBuilder()
            // Il cookie di sessione va inviato anche alla negotiate (l'hub richiede un utente autenticato)
            .WithUrl(Api.StatusHubUrl, options => options.HttpMessageHandlerFactory = inner => new IncludeCredentialsHandler(inner))
            .WithAutomaticReconnect(new ForeverRetryPolicy())
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();

        hub.On<DeviceStateChangedDto>(StatusHubMessages.DeviceStateChanged, change => InvokeAsync(() =>
        {
            foreach (var node in nodes)
            {
                if (node.DeviceId == change.DeviceId)
                    node.State = change.State;
                else
                    node.UpdateSubmapMember(change.DeviceId, change.State); // stato aggregato delle sottomappe dal vivo
            }
            StateHasChanged();
        }));

        hub.On<List<RouterOsSampleDto>>(StatusHubMessages.RouterOsUpdated, samples => InvokeAsync(() =>
        {
            foreach (var s in samples)
                routerOs[s.DeviceId] = s;
            var changed = samples.Select(s => s.DeviceId).ToHashSet();
            foreach (var node in nodes.Where(n => n.DeviceId is { } d && changed.Contains(d)))
                ApplyRouterOs(node);
            StateHasChanged();
        }));

        hub.On<List<InterfaceTrafficDto>>(StatusHubMessages.TrafficUpdated, samples => InvokeAsync(() =>
        {
            OnTrafficUpdated(samples);
            StateHasChanged();
        }));

        hub.Reconnecting += _ => SetLiveAsync(false);
        hub.Reconnected += async _ =>
        {
            await SetLiveAsync(true);
            await InvokeAsync(async () =>
            {
                await ReloadAsync();
                StateHasChanged();
            });
        };

        // La prima connessione non è coperta da WithAutomaticReconnect: riprova finché l'API non risponde
        var policy = new ForeverRetryPolicy();
        for (var attempt = 0; !ct.IsCancellationRequested; attempt++)
        {
            try
            {
                await hub.StartAsync(ct);
                await SetLiveAsync(true);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogWarning("Connessione all'hub degli stati fallita: {Message}", ex.Message);
                await Task.Delay(policy.Delay(attempt), ct);
            }
        }
    }

    private Task SetLiveAsync(bool connected) => InvokeAsync(() =>
    {
        liveConnected = connected;
        StateHasChanged();
    });

    /// <summary>Banner se un agente non invia report da più di 2 intervalli di snapshot (calcolato dall'API).</summary>
    private async Task MonitorAgentsAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(AgentCheckInterval);
        try
        {
            do
            {
                string? warning;
                try
                {
                    var agents = await Api.GetAgentsAsync(ct);
                    var offline = agents.Where(a => !a.IsOnline).ToList();
                    warning = agents.Count == 0
                        ? "Nessun agente di polling ha ancora inviato dati: gli stati dei nodi non sono disponibili."
                        : offline.Count > 0
                            ? "Agente non raggiungibile: " + string.Join(", ", offline.Select(a =>
                                $"{a.AgentId} (ultimo contatto {a.LastSeen.ToLocalTime():dd/MM HH:mm})")) +
                              ". Gli stati dei suoi dispositivi sono mostrati come sconosciuti."
                            : null;
                }
                catch (HttpRequestException)
                {
                    warning = null; // l'errore di API non raggiungibile è già mostrato dal caricamento della mappa
                }

                await InvokeAsync(() =>
                {
                    agentWarning = warning;
                    StateHasChanged();
                });
            }
            while (await timer.WaitForNextTickAsync(ct));
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Ridisegna ogni 5 s: i link senza campioni recenti passano a "n/d" anche se non arrivano messaggi.</summary>
    private async Task RefreshTrafficAgeAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await InvokeAsync(StateHasChanged);
        }
        catch (OperationCanceledException) { }
    }

    private async Task SavePositionAsync(MapNode node)
    {
        if (map is null) return;

        try
        {
            await Api.UpdateNodePositionAsync(map.Id, node.Id, node.X, node.Y, cts.Token);
            saveError = null;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (HttpRequestException ex)
        {
            Logger.LogError(ex, "Salvataggio posizione fallito per il nodo {NodeId}", node.Id);
            // Ricarica dal server per non mostrare una posizione che non è stata salvata
            await ReloadAsync();
            saveError = "Posizione non salvata: la mappa è stata ricaricata dal server.";
        }
    }

    private void ToggleEditMode()
    {
        editMode = !editMode;
        tool = MapTool.Move;
        selectedId = null;
        placePoint = null;
    }

    private void Select(Guid? id)
    {
        selectedId = id;
        placePoint = null;
    }

    private void PlaceAt(MapPoint point)
    {
        selectedId = null;
        placePoint = point;
    }

    private async Task PlaceAtCenterAsync()
    {
        if (mapView is not null)
            PlaceAt(await mapView.GetViewCenterAsync());
    }

    /// <summary>Link creato trascinando tra due nodi: 1 Gbps senza sorgente di traffico, da completare nel pannello.</summary>
    private async Task CreateLinkAsync(LinkRequest request)
    {
        if (map is null) return;

        try
        {
            var created = await Api.CreateLinkAsync(map.Id, new MapLinkUpsertDto(request.FromNodeId, request.ToNodeId), cts.Token);
            links.Add(ToModel(created));
            ApplyTraffic();
            saveError = null;
            Select(created.Id);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "Creazione del link fallita");
            saveError = $"Link non creato: {ex.Message}";
        }
    }

    /// <summary>Nodo creato o modificato dal pannello: sostituisce il modello locale con i dati del server.</summary>
    private void NodeSaved(MapNodeDto dto)
    {
        var model = ToModel(dto);
        var index = nodes.FindIndex(n => n.Id == dto.Id);
        if (index >= 0)
        {
            // La risposta del PUT non porta stato aggregato e manutenzione: restano quelli già noti
            var old = nodes[index];
            foreach (var (deviceId, member) in old.SubmapMembers)
                model.SubmapMembers.TryAdd(deviceId, member);
            model.RefreshSubmapState();
            model.Maintenance ??= old.Maintenance;
            ApplyRouterOs(model);
            nodes[index] = model;
        }
        else
            nodes.Add(model);

        Select(dto.Id);
    }

    private void NodeDeleted(Guid nodeId)
    {
        nodes.RemoveAll(n => n.Id == nodeId);
        links.RemoveAll(l => l.FromId == nodeId || l.ToId == nodeId); // CASCADE lato database
        Select(null);
    }

    private void LinkSaved(MapLinkDto dto)
    {
        var index = links.FindIndex(l => l.Id == dto.Id);
        if (index >= 0)
            links[index] = ToModel(dto);
        ApplyTraffic();
    }

    private void LinkDeleted(Guid linkId)
    {
        links.RemoveAll(l => l.Id == linkId);
        Select(null);
    }

    private Task OpenNode(MapNode node)
    {
        if (node.SubmapId is { } submapId)
            Navigation.NavigateTo($"map/{submapId}");
        else if (node.DeviceId is not null)
        {
            // Latenza e traffico del device sotto la mappa (come il doppio clic in The Dude)
            chartLinkId = null;
            chartNodeId = node.Id;
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        cts.Cancel();
        if (hub is not null)
            await hub.DisposeAsync();
        cts.Dispose();
    }

    /// <summary>Riconnessione senza limite di tentativi: 0, 2, 5, 10 s, poi ogni 30 s.</summary>
    private sealed class ForeverRetryPolicy : IRetryPolicy
    {
        private static readonly TimeSpan[] Delays =
            [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];

        public TimeSpan Delay(long attempt) => attempt < Delays.Length ? Delays[attempt] : TimeSpan.FromSeconds(30);

        public TimeSpan? NextRetryDelay(RetryContext retryContext) => Delay(retryContext.PreviousRetryCount);
    }
}
