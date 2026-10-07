// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Worker.Api;

public enum SendOutcome { Sent, Retry, Unauthorized, Rejected }

/// <summary>Client HTTP verso gli endpoint /api/agent dell'API (header X-Agent-Key impostato in Program.cs).</summary>
public sealed class AgentApiClient(HttpClient http, ILogger<AgentApiClient> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    /// <exception cref="HttpRequestException">API non raggiungibile o risposta di errore.</exception>
    public async Task<IReadOnlyList<AgentTargetDto>> GetTargetsAsync(CancellationToken ct) =>
        await http.GetFromJsonAsync<List<AgentTargetDto>>("api/agent/targets", Json, ct) ?? [];

    /// <summary>Invia l'inventario delle interfacce; false se l'API non lo ha accettato (si riproverà più tardi).</summary>
    public async Task<bool> SendInterfacesAsync(AgentInterfacesReportDto report, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("api/agent/interfaces", report, Json, ct);
            if (response.IsSuccessStatusCode)
                return true;

            logger.LogWarning("Inventario interfacce rifiutato dall'API ({Status})", (int)response.StatusCode);
            return false;
        }
        catch (HttpRequestException ex)
        {
            logger.LogDebug("Inventario interfacce non inviato, API non raggiungibile: {Message}", ex.Message);
            return false;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogDebug("Inventario interfacce non inviato: timeout");
            return false;
        }
    }

    /// <summary>Invio "best effort" del traffico live: in caso di errore si logga e si scarta.</summary>
    public async Task SendTrafficAsync(AgentTrafficReportDto report, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("api/agent/traffic", report, Json, ct);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Traffico rifiutato dall'API ({Status}): {Count} campioni scartati", (int)response.StatusCode, report.Samples.Count);
        }
        catch (HttpRequestException ex)
        {
            logger.LogDebug("Traffico non inviato, API non raggiungibile: {Message}", ex.Message);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogDebug("Traffico non inviato: timeout");
        }
    }

    /// <summary>Invia i vicini di un device; false se l'API non li ha accettati (si riproverà più tardi).</summary>
    public async Task<bool> SendNeighborsAsync(AgentNeighborsReportDto report, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("api/agent/neighbors", report, Json, ct);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Vicini rifiutati dall'API ({Status})", (int)response.StatusCode);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            logger.LogDebug("Vicini non inviati, API non raggiungibile: {Message}", ex.Message);
            return false;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogDebug("Vicini non inviati: timeout");
            return false;
        }
    }

    /// <summary>Compito di scansione; null se la scansione non esiste più o è già stata presa da un altro agente (409).</summary>
    public async Task<AgentScanRequestDto?> GetScanAsync(Guid scanId, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync($"api/agent/scans/{scanId}", ct);
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<AgentScanRequestDto>(Json, ct) : null;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Scansione {ScanId} non letta: {Message}", scanId, ex.Message);
            return null;
        }
    }

    public async Task SendScanResultAsync(Guid scanId, AgentScanResultDto result, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync($"api/agent/scans/{scanId}", result, Json, ct);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Esito della scansione rifiutato dall'API ({Status})", (int)response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Esito della scansione non inviato: {Message}", ex.Message);
        }
    }

    /// <summary>Invio "best effort" delle letture RouterOS (dato live + storico): in caso di errore si scarta.</summary>
    public async Task SendRouterOsAsync(AgentRouterOsReportDto report, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("api/agent/routeros", report, Json, ct);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Letture RouterOS rifiutate dall'API ({Status}): {Count} scartate", (int)response.StatusCode, report.Samples.Count);
        }
        catch (HttpRequestException ex)
        {
            logger.LogDebug("Letture RouterOS non inviate, API non raggiungibile: {Message}", ex.Message);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogDebug("Letture RouterOS non inviate: timeout");
        }
    }

    public async Task<SendOutcome> SendStatusAsync(AgentStatusReportDto report, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("api/agent/status", report, Json, ct);
            if (response.IsSuccessStatusCode)
                return SendOutcome.Sent;

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return SendOutcome.Unauthorized;

            // 5xx, 408, 429: problema temporaneo. Altri 4xx: report non accettabile, ritrasmetterlo non serve
            if ((int)response.StatusCode >= 500
                || response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests)
                return SendOutcome.Retry;

            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogError("Report rifiutato dall'API ({Status}): {Body}", (int)response.StatusCode, body);
            return SendOutcome.Rejected;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("API non raggiungibile: {Message}", ex.Message);
            return SendOutcome.Retry;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Timeout nell'invio del report all'API");
            return SendOutcome.Retry;
        }
    }
}
