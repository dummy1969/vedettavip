// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Http.HttpResults;
using VedettaVip.Api.Services;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Endpoints;

public static class MetricEndpoints
{
    private static readonly TimeSpan DefaultRange = TimeSpan.FromHours(6);
    private static readonly TimeSpan MaxRange = TimeSpan.FromDays(731);

    public static IEndpointRouteBuilder MapMetricEndpoints(this IEndpointRouteBuilder app)
    {
        var metrics = app.MapGroup("/api/metrics").WithTags("Metrics");
        metrics.MapGet("/interface", GetInterfaceTrafficAsync);
        metrics.MapGet("/device", GetDeviceLatencyAsync);
        metrics.MapGet("/device/interfaces", GetInterfacesWithHistoryAsync);
        metrics.MapGet("/device/routeros", GetDeviceRouterOsAsync);
        return app;
    }

    /// <summary>
    /// Traffico storico di un'interfaccia. from/to in ISO 8601 (default: ultime 6 ore); sorgente e bucket
    /// scelti in base all'intervallo (circa 500 punti), bucket senza dati restituiti con valori null.
    /// </summary>
    private static async Task<Results<Ok<InterfaceSeriesDto>, ValidationProblem>> GetInterfaceTrafficAsync(
        Guid deviceId, int ifIndex, DateTimeOffset? from, DateTimeOffset? to,
        MetricQuery query, TimeProvider time, CancellationToken ct)
    {
        var end = to ?? time.GetUtcNow();
        var start = from ?? end - DefaultRange;

        if (Invalid(start, end) is { } problem)
            return problem;

        return TypedResults.Ok(await query.InterfaceTrafficAsync(deviceId, ifIndex, start, end, ct));
    }

    /// <summary>Latenza (RTT medio e massimo dei ping riusciti) e perdita % di un device; stesse regole di periodo.</summary>
    private static async Task<Results<Ok<DeviceLatencySeriesDto>, ValidationProblem>> GetDeviceLatencyAsync(
        Guid deviceId, DateTimeOffset? from, DateTimeOffset? to, MetricQuery query, TimeProvider time, CancellationToken ct)
    {
        var end = to ?? time.GetUtcNow();
        var start = from ?? end - DefaultRange;
        if (Invalid(start, end) is { } problem)
            return problem;

        return TypedResults.Ok(await query.DeviceLatencyAsync(deviceId, start, end, ct));
    }

    /// <summary>CPU (media e picco), memoria e temperatura di un router RouterOS; stesse regole di periodo.</summary>
    private static async Task<Results<Ok<DeviceRouterOsSeriesDto>, ValidationProblem>> GetDeviceRouterOsAsync(
        Guid deviceId, DateTimeOffset? from, DateTimeOffset? to, MetricQuery query, TimeProvider time, CancellationToken ct)
    {
        var end = to ?? time.GetUtcNow();
        var start = from ?? end - DefaultRange;
        if (Invalid(start, end) is { } problem)
            return problem;

        return TypedResults.Ok(await query.DeviceRouterOsAsync(deviceId, start, end, ct));
    }

    /// <summary>Interfacce del device che hanno storico del traffico (ultimi 90 giorni).</summary>
    private static async Task<Ok<List<InterfaceWithHistoryDto>>> GetInterfacesWithHistoryAsync(
        Guid deviceId, MetricQuery query, CancellationToken ct) =>
        TypedResults.Ok(await query.InterfacesWithHistoryAsync(deviceId, ct));

    private static ValidationProblem? Invalid(DateTimeOffset start, DateTimeOffset end) =>
        start >= end || end - start > MaxRange
            ? TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["from"] = [$"Intervallo non valido: from deve precedere to, al massimo {MaxRange.TotalDays:0} giorni."]
            })
            : null;
}
