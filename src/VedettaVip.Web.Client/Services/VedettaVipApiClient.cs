// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Web.Client.Services;

/// <summary>Client tipizzato verso VedettaVip.Api (chiamate dirette dal browser, CORS abilitato lato API).</summary>
public sealed class VedettaVipApiClient(HttpClient http)
{
    /// <summary>URL dell'hub SignalR degli stati (stesso host dell'API).</summary>
    public Uri StatusHubUrl => new(http.BaseAddress!, StatusHubMessages.HubPath);

    // Stesse convenzioni dell'API: camelCase ed enum come stringhe
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public async Task<IReadOnlyList<MapSummaryDto>> GetMapsAsync(CancellationToken ct) =>
        await http.GetFromJsonAsync<List<MapSummaryDto>>("api/maps", Json, ct) ?? [];

    public async Task<IReadOnlyList<AgentDto>> GetAgentsAsync(CancellationToken ct) =>
        await http.GetFromJsonAsync<List<AgentDto>>("api/agents", Json, ct) ?? [];

    /// <summary>Traffico storico di un'interfaccia; sorgente e risoluzione le sceglie l'API in base al periodo.</summary>
    public Task<InterfaceSeriesDto> GetInterfaceTrafficAsync(Guid deviceId, int ifIndex, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        SendAsync<InterfaceSeriesDto>(HttpMethod.Get,
            $"api/metrics/interface?deviceId={deviceId}&ifIndex={ifIndex}" +
            $"&from={Uri.EscapeDataString(from.UtcDateTime.ToString("O"))}&to={Uri.EscapeDataString(to.UtcDateTime.ToString("O"))}",
            null, ct);

    /// <summary>Latenza (RTT) e perdita storiche di un device.</summary>
    public Task<DeviceRouterOsSeriesDto> GetDeviceRouterOsAsync(Guid deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        SendAsync<DeviceRouterOsSeriesDto>(HttpMethod.Get,
            $"api/metrics/device/routeros?deviceId={deviceId}" +
            $"&from={Uri.EscapeDataString(from.UtcDateTime.ToString("O"))}&to={Uri.EscapeDataString(to.UtcDateTime.ToString("O"))}",
            null, ct);

    /// <summary>Interfacce e peer WireGuard dell'ultima lettura di un router e quelli sorvegliati.</summary>
    public Task<DeviceRouterOsDetailDto> GetDeviceRouterOsDetailAsync(Guid deviceId, CancellationToken ct) =>
        SendAsync<DeviceRouterOsDetailDto>(HttpMethod.Get, $"api/devices/{deviceId}/routeros", null, ct);

    public async Task<IReadOnlyList<RouterOsWatchDto>> ToggleRouterOsWatchAsync(Guid deviceId, RouterOsWatchToggleDto dto, CancellationToken ct) =>
        await SendAsync<List<RouterOsWatchDto>>(HttpMethod.Put, $"api/devices/{deviceId}/routeros/watches", dto, ct);

    /// <summary>Ultime letture RouterOS (CPU, memoria, temperatura...) dei router con API abilitata.</summary>
    public async Task<IReadOnlyList<RouterOsSampleDto>> GetRouterOsAsync(CancellationToken ct) =>
        await SendAsync<List<RouterOsSampleDto>>(HttpMethod.Get, "api/routeros", null, ct);

    public async Task<IReadOnlyList<RouterOsCredentialDto>> GetRouterOsCredentialsAsync(CancellationToken ct) =>
        await SendAsync<List<RouterOsCredentialDto>>(HttpMethod.Get, "api/routeros-credentials", null, ct);

    public Task<RouterOsCredentialDto> CreateRouterOsCredentialAsync(RouterOsCredentialUpsertDto dto, CancellationToken ct) =>
        SendAsync<RouterOsCredentialDto>(HttpMethod.Post, "api/routeros-credentials", dto, ct);

    public Task UpdateRouterOsCredentialAsync(Guid id, RouterOsCredentialUpsertDto dto, CancellationToken ct) =>
        SendAsync(HttpMethod.Put, $"api/routeros-credentials/{id}", dto, ct);

    public Task DeleteRouterOsCredentialAsync(Guid id, CancellationToken ct) =>
        SendAsync(HttpMethod.Delete, $"api/routeros-credentials/{id}", null, ct);

    public Task SetDefaultRouterOsCredentialAsync(Guid? id, CancellationToken ct) =>
        SendAsync(HttpMethod.Put, "api/routeros-credentials/default", new RouterOsDefaultDto(id), ct);

    public Task<DeviceLatencySeriesDto> GetDeviceLatencyAsync(Guid deviceId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        SendAsync<DeviceLatencySeriesDto>(HttpMethod.Get,
            $"api/metrics/device?deviceId={deviceId}" +
            $"&from={Uri.EscapeDataString(from.UtcDateTime.ToString("O"))}&to={Uri.EscapeDataString(to.UtcDateTime.ToString("O"))}",
            null, ct);

    /// <summary>Interfacce del device con storico del traffico (ultimi 90 giorni).</summary>
    public async Task<IReadOnlyList<InterfaceWithHistoryDto>> GetInterfacesWithHistoryAsync(Guid deviceId, CancellationToken ct) =>
        await SendAsync<List<InterfaceWithHistoryDto>>(HttpMethod.Get, $"api/metrics/device/interfaces?deviceId={deviceId}", null, ct);

    /// <summary>Ultimo campione di traffico per interfaccia (solo quelli recenti).</summary>
    public async Task<IReadOnlyList<InterfaceTrafficDto>> GetTrafficAsync(CancellationToken ct) =>
        await http.GetFromJsonAsync<List<InterfaceTrafficDto>>("api/traffic", Json, ct) ?? [];

    public async Task<IReadOnlyList<DeviceDto>> GetDevicesAsync(CancellationToken ct) =>
        await http.GetFromJsonAsync<List<DeviceDto>>("api/devices", Json, ct) ?? [];

    // ---------- Autenticazione e utenti ----------

    public Task<CurrentUserDto> GetMeAsync(CancellationToken ct) =>
        SendAsync<CurrentUserDto>(HttpMethod.Get, "api/auth/me", null, ct);

    public Task LoginAsync(LoginDto dto, CancellationToken ct) => SendAsync(HttpMethod.Post, "api/auth/login", dto, ct);

    public Task LogoutAsync(CancellationToken ct) => SendAsync(HttpMethod.Post, "api/auth/logout", null, ct);

    public Task SetupAsync(SetupDto dto, CancellationToken ct) => SendAsync(HttpMethod.Post, "api/auth/setup", dto, ct);

    public Task ChangePasswordAsync(ChangePasswordDto dto, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, "api/auth/change-password", dto, ct);

    public async Task<IReadOnlyList<UserDto>> GetUsersAsync(CancellationToken ct) =>
        await SendAsync<List<UserDto>>(HttpMethod.Get, "api/users", null, ct);

    public Task<UserDto> CreateUserAsync(UserCreateDto dto, CancellationToken ct) =>
        SendAsync<UserDto>(HttpMethod.Post, "api/users", dto, ct);

    public Task<UserDto> UpdateUserAsync(Guid id, UserUpdateDto dto, CancellationToken ct) =>
        SendAsync<UserDto>(HttpMethod.Put, $"api/users/{id}", dto, ct);

    public Task ResetUserPasswordAsync(Guid id, string newPassword, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, $"api/users/{id}/reset-password", new ResetPasswordDto(newPassword), ct);

    public Task DeleteUserAsync(Guid id, CancellationToken ct) => SendAsync(HttpMethod.Delete, $"api/users/{id}", null, ct);

    // ---------- Notifiche ----------

    public Task<NotificationSettingsDto> GetNotificationSettingsAsync(CancellationToken ct) =>
        SendAsync<NotificationSettingsDto>(HttpMethod.Get, "api/settings/notifications", null, ct);

    public Task<NotificationSettingsDto> UpdateNotificationSettingsAsync(NotificationSettingsUpdateDto dto, CancellationToken ct) =>
        SendAsync<NotificationSettingsDto>(HttpMethod.Put, "api/settings/notifications", dto, ct);

    /// <summary>Invia un'email di prova con le impostazioni salvate; l'errore del server SMTP arriva nell'eccezione.</summary>
    public Task SendTestEmailAsync(string to, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, "api/settings/notifications/test-email", new TestEmailDto(to), ct);

    public Task SendTestTelegramAsync(string chatId, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, "api/settings/notifications/test-telegram", new TestTelegramDto(chatId), ct);

    /// <summary>Anteprima (DryRun) o import vero dei dispositivi da CSV.</summary>
    public Task<DeviceImportResultDto> ImportDevicesAsync(DeviceImportRequestDto dto, CancellationToken ct) =>
        SendAsync<DeviceImportResultDto>(HttpMethod.Post, "api/devices/import", dto, ct);

    /// <summary>URL del CSV di tutti i dispositivi (link diretto: il cookie di sessione viaggia con la navigazione).</summary>
    public string DeviceExportUrl => new Uri(http.BaseAddress!, "api/devices/export").ToString();

    public async Task<IReadOnlyList<SnmpCredentialDto>> GetSnmpCredentialsAsync(CancellationToken ct) =>
        await SendAsync<List<SnmpCredentialDto>>(HttpMethod.Get, "api/snmp-credentials", null, ct);

    public Task<SnmpCredentialDto> CreateSnmpCredentialAsync(SnmpCredentialUpsertDto dto, CancellationToken ct) =>
        SendAsync<SnmpCredentialDto>(HttpMethod.Post, "api/snmp-credentials", dto, ct);

    public Task UpdateSnmpCredentialAsync(Guid id, SnmpCredentialUpsertDto dto, CancellationToken ct) =>
        SendAsync(HttpMethod.Put, $"api/snmp-credentials/{id}", dto, ct);

    public Task DeleteSnmpCredentialAsync(Guid id, CancellationToken ct) =>
        SendAsync(HttpMethod.Delete, $"api/snmp-credentials/{id}", null, ct);

    public Task SetDefaultSnmpCredentialAsync(Guid? id, CancellationToken ct) =>
        SendAsync(HttpMethod.Put, "api/snmp-credentials/default", new SnmpDefaultDto(id), ct);

    public async Task<IReadOnlyList<CustomerDto>> GetCustomersAsync(CancellationToken ct) =>
        await SendAsync<List<CustomerDto>>(HttpMethod.Get, "api/customers", null, ct);

    public Task<CustomerDto> CreateCustomerAsync(CustomerUpsertDto dto, CancellationToken ct) =>
        SendAsync<CustomerDto>(HttpMethod.Post, "api/customers", dto, ct);

    public Task UpdateCustomerAsync(Guid id, CustomerUpsertDto dto, CancellationToken ct) =>
        SendAsync(HttpMethod.Put, $"api/customers/{id}", dto, ct);

    public Task DeleteCustomerAsync(Guid id, CancellationToken ct) =>
        SendAsync(HttpMethod.Delete, $"api/customers/{id}", null, ct);

    public async Task<IReadOnlyList<ContactDto>> GetContactsAsync(CancellationToken ct) =>
        await SendAsync<List<ContactDto>>(HttpMethod.Get, "api/contacts", null, ct);

    public Task<ContactDto> CreateContactAsync(ContactUpsertDto dto, CancellationToken ct) =>
        SendAsync<ContactDto>(HttpMethod.Post, "api/contacts", dto, ct);

    public Task<ContactDto> UpdateContactAsync(Guid id, ContactUpsertDto dto, CancellationToken ct) =>
        SendAsync<ContactDto>(HttpMethod.Put, $"api/contacts/{id}", dto, ct);

    public Task DeleteContactAsync(Guid id, CancellationToken ct) =>
        SendAsync(HttpMethod.Delete, $"api/contacts/{id}", null, ct);

    /// <summary>
    /// Eventi più recenti; <paramref name="before"/> per caricare i precedenti. Filtri facoltativi per device, cliente
    /// (<paramref name="noCustomer"/> = device senza cliente ed eventi degli agenti), tipo e inizio del periodo.
    /// </summary>
    public async Task<IReadOnlyList<EventDto>> GetEventsAsync(bool unacknowledgedOnly, DateTimeOffset? before, int limit, CancellationToken ct,
        DateTimeOffset? since = null, Guid? deviceId = null, Guid? customerId = null, bool noCustomer = false,
        EventSeverity? severity = null)
    {
        var url = $"api/events?unacknowledgedOnly={(unacknowledgedOnly ? "true" : "false")}&limit={limit}";
        if (deviceId is { } d)
            url += $"&deviceId={d}";
        if (severity is { } sev)
            url += $"&severity={sev}";
        if (customerId is { } c)
            url += $"&customerId={c}";
        else if (noCustomer)
            url += "&noCustomer=true";
        if (before is { } b)
            url += $"&before={Uri.EscapeDataString(b.UtcDateTime.ToString("O"))}";
        if (since is { } s)
            url += $"&since={Uri.EscapeDataString(s.UtcDateTime.ToString("O"))}";
        return await SendAsync<List<EventDto>>(HttpMethod.Get, url, null, ct);
    }

    public Task AcknowledgeEventAsync(long id, bool acknowledged, CancellationToken ct) =>
        SendAsync(HttpMethod.Put, $"api/events/{id}/ack", new EventAckDto(acknowledged), ct);

    public async Task<IReadOnlyList<DeliveryDto>> GetDeliveriesAsync(long eventId, CancellationToken ct) =>
        await SendAsync<List<DeliveryDto>>(HttpMethod.Get, $"api/events/{eventId}/deliveries", null, ct);

    // ---------- Soglie sulle metriche e manutenzione ----------

    public Task<DashboardSettingsDto> GetDashboardSettingsAsync(CancellationToken ct) =>
        SendAsync<DashboardSettingsDto>(HttpMethod.Get, "api/settings/dashboard", null, ct);

    public Task<DashboardSettingsDto> UpdateDashboardSettingsAsync(DashboardSettingsDto dto, CancellationToken ct) =>
        SendAsync<DashboardSettingsDto>(HttpMethod.Put, "api/settings/dashboard", dto, ct);

    public Task<WinBoxSettingsDto> GetWinBoxSettingsAsync(CancellationToken ct) =>
        SendAsync<WinBoxSettingsDto>(HttpMethod.Get, "api/settings/winbox", null, ct);

    public Task<WinBoxSettingsDto> UpdateWinBoxSettingsAsync(WinBoxSettingsDto dto, CancellationToken ct) =>
        SendAsync<WinBoxSettingsDto>(HttpMethod.Put, "api/settings/winbox", dto, ct);

    /// <summary>Download dell'installer del gestore winbox:// ("windows" o "linux"), con il percorso delle Impostazioni.</summary>
    public string WinBoxHandlerUrl(string os) => new Uri(http.BaseAddress!, $"api/winbox/handler/{os}").ToString();

    public Task<MetricThresholdsDto> GetMetricThresholdsAsync(CancellationToken ct) =>
        SendAsync<MetricThresholdsDto>(HttpMethod.Get, "api/settings/metric-thresholds", null, ct);

    public Task<MetricThresholdsDto> UpdateMetricThresholdsAsync(MetricThresholdsDto dto, CancellationToken ct) =>
        SendAsync<MetricThresholdsDto>(HttpMethod.Put, "api/settings/metric-thresholds", dto, ct);

    public async Task<IReadOnlyList<MaintenanceWindowDto>> GetMaintenanceWindowsAsync(CancellationToken ct) =>
        await SendAsync<List<MaintenanceWindowDto>>(HttpMethod.Get, "api/maintenance", null, ct);

    public Task<MaintenanceWindowDto> CreateMaintenanceWindowAsync(MaintenanceWindowUpsertDto dto, CancellationToken ct) =>
        SendAsync<MaintenanceWindowDto>(HttpMethod.Post, "api/maintenance", dto, ct);

    public Task<MaintenanceWindowDto> UpdateMaintenanceWindowAsync(Guid id, MaintenanceWindowUpsertDto dto, CancellationToken ct) =>
        SendAsync<MaintenanceWindowDto>(HttpMethod.Put, $"api/maintenance/{id}", dto, ct);

    public Task DeleteMaintenanceWindowAsync(Guid id, CancellationToken ct) =>
        SendAsync(HttpMethod.Delete, $"api/maintenance/{id}", null, ct);

    /// <summary>Soglie generali di rilevazione (pagina Impostazioni).</summary>
    public Task<DetectionThresholdsDto> GetDetectionSettingsAsync(CancellationToken ct) =>
        SendAsync<DetectionThresholdsDto>(HttpMethod.Get, "api/settings/detection", null, ct);

    public Task<DetectionThresholdsDto> UpdateDetectionSettingsAsync(DetectionThresholdsDto dto, CancellationToken ct) =>
        SendAsync<DetectionThresholdsDto>(HttpMethod.Put, "api/settings/detection", dto, ct);

    public Task<DiscoveryResultDto> GetDiscoveryAsync(CancellationToken ct) =>
        SendAsync<DiscoveryResultDto>(HttpMethod.Get, "api/discovery", null, ct);

    public Task<DiscoveryAcceptResultDto> AcceptDiscoveryAsync(DiscoveryAcceptDto dto, CancellationToken ct) =>
        SendAsync<DiscoveryAcceptResultDto>(HttpMethod.Post, "api/discovery/accept", dto, ct);

    public Task IgnoreDiscoveryAsync(DiscoveryIgnoreDto dto, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, "api/discovery/ignore", dto, ct);

    public Task<DiscoveryForgetResultDto> ForgetDiscoveryAsync(DiscoveryForgetDto dto, CancellationToken ct) =>
        SendAsync<DiscoveryForgetResultDto>(HttpMethod.Post, "api/discovery/forget", dto, ct);

    public async Task<IReadOnlyList<DiscoveryScanDto>> GetScansAsync(CancellationToken ct) =>
        await SendAsync<List<DiscoveryScanDto>>(HttpMethod.Get, "api/discovery/scans", null, ct);

    public Task<DiscoveryScanDto> CreateScanAsync(DiscoveryScanCreateDto dto, CancellationToken ct) =>
        SendAsync<DiscoveryScanDto>(HttpMethod.Post, "api/discovery/scans", dto, ct);

    public Task DeleteScanAsync(Guid id, CancellationToken ct) => SendAsync(HttpMethod.Delete, $"api/discovery/scans/{id}", null, ct);

    /// <summary>"Scopri ora": gli agenti rileggono subito i vicini di tutti i dispositivi.</summary>
    public Task RefreshDiscoveryAsync(CancellationToken ct) =>
        SendAsync(HttpMethod.Post, "api/discovery/refresh", null, ct);

    public async Task<IReadOnlyList<DeviceOverviewDto>> GetDeviceOverviewAsync(CancellationToken ct) =>
        await http.GetFromJsonAsync<List<DeviceOverviewDto>>("api/devices/overview", Json, ct) ?? [];

    /// <summary>Interfacce dall'ultimo inventario SNMP dell'agente (vuoto se mai lette).</summary>
    public Task<DeviceInterfacesDto> GetDeviceInterfacesAsync(Guid id, CancellationToken ct) =>
        SendAsync<DeviceInterfacesDto>(HttpMethod.Get, $"api/devices/{id}/interfaces", null, ct);

    /// <summary>Chiede all'agente di rileggere subito le interfacce (risultato su GetDeviceInterfacesAsync in pochi secondi).</summary>
    public Task RefreshDeviceInterfacesAsync(Guid id, CancellationToken ct) =>
        SendAsync(HttpMethod.Post, $"api/devices/{id}/interfaces/refresh", null, ct);

    public Task<DeviceDto> GetDeviceAsync(Guid id, CancellationToken ct) =>
        SendAsync<DeviceDto>(HttpMethod.Get, $"api/devices/{id}", null, ct);

    /// <summary>Crea il device; con <paramref name="mapId"/> lo aggiunge anche a quella mappa (posizione scelta dall'API).</summary>
    public Task<DeviceDto> CreateDeviceAsync(DeviceUpsertDto dto, CancellationToken ct, Guid? mapId = null) =>
        SendAsync<DeviceDto>(HttpMethod.Post, mapId is { } m ? $"api/devices?mapId={m}" : "api/devices", dto, ct);

    public Task UpdateDeviceAsync(Guid id, DeviceUpsertDto dto, CancellationToken ct) =>
        SendAsync(HttpMethod.Put, $"api/devices/{id}", dto, ct);

    /// <summary>
    /// Restituisce null se eliminato, oppure il motivo per cui il dispositivo è ancora in uso (409).
    /// Con <paramref name="purgeEvents"/> cancella anche lo storico degli eventi (le mappe bloccano comunque).
    /// </summary>
    public async Task<DeviceInUseDto?> DeleteDeviceAsync(Guid id, CancellationToken ct, bool purgeEvents = false)
    {
        using var response = await http.DeleteAsync($"api/devices/{id}{(purgeEvents ? "?purgeEvents=true" : "")}", ct);
        if (response.StatusCode == HttpStatusCode.Conflict
            && await TryReadAsync<DeviceInUseDto>(response, ct) is { Maps: not null } inUse) // un 409 ProblemDetails (race) non ha Maps
            return inUse;

        if (!response.IsSuccessStatusCode)
            throw await ApiProblemException.FromResponseAsync(response, ct);
        return null;
    }

    /// <summary>Restituisce null se la mappa non esiste.</summary>
    public async Task<MapDto?> GetMapAsync(Guid mapId, CancellationToken ct)
    {
        using var response = await http.GetAsync($"api/maps/{mapId}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MapDto>(Json, ct);
    }

    public Task UpdateMapAsync(Guid id, MapUpsertDto dto, CancellationToken ct) =>
        SendAsync(HttpMethod.Put, $"api/maps/{id}", dto, ct);

    /// <summary>Elimina la mappa con nodi e link; 409 se è ancora rappresentata da un nodo Submap.</summary>
    public Task DeleteMapAsync(Guid id, CancellationToken ct) => SendAsync(HttpMethod.Delete, $"api/maps/{id}", null, ct);

    public Task<MapDto> CreateMapAsync(MapUpsertDto dto, CancellationToken ct) =>
        SendAsync<MapDto>(HttpMethod.Post, "api/maps", dto, ct);

    public Task<MapNodeDto> CreateNodeAsync(Guid mapId, CreateMapNodeDto dto, CancellationToken ct) =>
        SendAsync<MapNodeDto>(HttpMethod.Post, $"api/maps/{mapId}/nodes", dto, ct);

    public Task<MapNodeDto> UpdateNodeAsync(Guid mapId, Guid nodeId, UpdateMapNodeDto dto, CancellationToken ct) =>
        SendAsync<MapNodeDto>(HttpMethod.Put, $"api/maps/{mapId}/nodes/{nodeId}", dto, ct);

    public Task UpdateNodePositionAsync(Guid mapId, Guid nodeId, double x, double y, CancellationToken ct) =>
        SendAsync(HttpMethod.Put, $"api/maps/{mapId}/nodes/{nodeId}/position", new NodePositionDto(x, y), ct);

    public Task DeleteNodeAsync(Guid mapId, Guid nodeId, CancellationToken ct) =>
        SendAsync(HttpMethod.Delete, $"api/maps/{mapId}/nodes/{nodeId}", null, ct);

    public Task<MapLinkDto> CreateLinkAsync(Guid mapId, MapLinkUpsertDto dto, CancellationToken ct) =>
        SendAsync<MapLinkDto>(HttpMethod.Post, $"api/maps/{mapId}/links", dto, ct);

    public Task UpdateLinkAsync(Guid mapId, Guid linkId, MapLinkUpsertDto dto, CancellationToken ct) =>
        SendAsync(HttpMethod.Put, $"api/maps/{mapId}/links/{linkId}", dto, ct);

    public Task DeleteLinkAsync(Guid mapId, Guid linkId, CancellationToken ct) =>
        SendAsync(HttpMethod.Delete, $"api/maps/{mapId}/links/{linkId}", null, ct);

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        using var response = await SendCoreAsync(method, url, body, ct);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct)
               ?? throw new HttpRequestException($"Risposta vuota da {url}.");
    }

    private async Task SendAsync(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        using var response = await SendCoreAsync(method, url, body, ct);
    }

    /// <summary>Legge il corpo come <typeparamref name="T"/>; null se non è JSON o non ha la forma attesa.</summary>
    private static async Task<T?> TryReadAsync<T>(HttpResponseMessage response, CancellationToken ct) where T : class
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Json, ct);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);

        var response = await http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode)
            return response;

        using (response)
            throw await ApiProblemException.FromResponseAsync(response, ct);
    }
}

/// <summary>
/// Errore dell'API con il messaggio leggibile estratto dal ProblemDetails (title, detail, errori di validazione).
/// Deriva da HttpRequestException: i catch esistenti continuano a funzionare.
/// </summary>
public sealed class ApiProblemException(string message, HttpStatusCode statusCode)
    : HttpRequestException(message, null, statusCode)
{
    public static async Task<ApiProblemException> FromResponseAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var status = response.StatusCode;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<Problem>(ct);
            if (problem is not null)
            {
                var parts = new List<string>();
                if (problem.Errors is { Count: > 0 })
                    parts.AddRange(problem.Errors.SelectMany(e => e.Value));
                else
                {
                    if (!string.IsNullOrWhiteSpace(problem.Title)) parts.Add(problem.Title);
                    if (!string.IsNullOrWhiteSpace(problem.Detail)) parts.Add(problem.Detail);
                }
                if (parts.Count > 0)
                    return new ApiProblemException(string.Join(" ", parts), status);
            }
        }
        catch (JsonException) { }
        catch (NotSupportedException) { } // content type non JSON

        return new ApiProblemException(status switch
        {
            HttpStatusCode.Forbidden => "Permessi insufficienti per questa operazione.",
            HttpStatusCode.Unauthorized => "Sessione scaduta: accedi di nuovo.",
            _ => $"Errore dell'API ({(int)status} {status})."
        }, status);
    }

    private sealed record Problem(string? Title, string? Detail, Dictionary<string, string[]>? Errors);
}
