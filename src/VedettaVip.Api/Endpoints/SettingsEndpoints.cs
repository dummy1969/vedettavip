// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using VedettaVip.Api.Data;
using VedettaVip.Api.Security;
using VedettaVip.Api.Data.Entities;
using VedettaVip.Api.Services;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Endpoints;

/// <summary>Impostazioni generali del monitoraggio (pagina Impostazioni).</summary>
public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var settings = app.MapGroup("/api/settings").WithTags("Settings");
        settings.MapGet("/detection", GetDetectionAsync);
        settings.MapPut("/detection", UpdateDetectionAsync).RequireAdmin();
        settings.MapGet("/metric-thresholds", GetMetricThresholdsAsync);
        settings.MapPut("/metric-thresholds", UpdateMetricThresholdsAsync).RequireAdmin();
        settings.MapGet("/dashboard", GetDashboardAsync);
        settings.MapPut("/dashboard", UpdateDashboardAsync).RequireAdmin();
        settings.MapGet("/winbox", GetWinBoxAsync);
        settings.MapPut("/winbox", UpdateWinBoxAsync).RequireAdmin();
        return app;
    }

    /// <summary>Soglie generali di rilevazione; la riga è creata dalla migration (Id = 1).</summary>
    internal static async Task<DetectionThresholdsDto> LoadDetectionAsync(VedettaVipDbContext db, CancellationToken ct) =>
        await db.MonitoringSettings.AsNoTracking()
            .Where(s => s.Id == MonitoringSettings.SingletonId)
            .Select(s => new DetectionThresholdsDto(s.DownAfterFailures, s.UpAfterSuccesses, s.SnmpDegradedAfterFailures))
            .FirstOrDefaultAsync(ct)
        ?? DetectionThresholdsDto.Default;

    private static async Task<Ok<DetectionThresholdsDto>> GetDetectionAsync(VedettaVipDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await LoadDetectionAsync(db, ct));

    /// <summary>Soglie generali sulle metriche (latenza, perdita, utilizzo dei link) e finestra di valutazione.</summary>
    private static async Task<Ok<MetricThresholdsDto>> GetMetricThresholdsAsync(VedettaVipDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.MonitoringSettings.AsNoTracking()
            .Where(s => s.Id == MonitoringSettings.SingletonId)
            .Select(s => new MetricThresholdsDto(s.RttThresholdMs, s.LossThresholdPct, s.LinkUtilizationThresholdPct, s.ThresholdWindowMinutes,
                s.RouterOsCpuThresholdPct, s.RouterOsTemperatureThresholdC))
            .FirstOrDefaultAsync(ct) ?? MetricThresholdsDto.Default);

    /// <summary>Le nuove soglie valgono dalla prossima valutazione (entro un minuto); una soglia disattivata chiude i suoi avvisi.</summary>
    private static async Task<Results<Ok<MetricThresholdsDto>, ProblemHttpResult>> UpdateMetricThresholdsAsync(
        MetricThresholdsDto dto, VedettaVipDbContext db, CancellationToken ct)
    {
        var settings = await db.MonitoringSettings.FirstAsync(s => s.Id == MonitoringSettings.SingletonId, ct);
        settings.RttThresholdMs = dto.RttMs;
        settings.LossThresholdPct = dto.LossPct;
        settings.LinkUtilizationThresholdPct = dto.LinkUtilizationPct;
        settings.ThresholdWindowMinutes = dto.WindowMinutes;
        settings.RouterOsCpuThresholdPct = dto.CpuPct;
        settings.RouterOsTemperatureThresholdC = dto.TemperatureC;
        if (await db.TrySaveChangesAsync(ct) is { } problem)
            return problem;
        return TypedResults.Ok(dto);
    }

    /// <summary>Impostazioni della dashboard (lette da tutti gli utenti, modificate dagli Admin).</summary>
    private static async Task<Ok<DashboardSettingsDto>> GetDashboardAsync(VedettaVipDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.MonitoringSettings.AsNoTracking()
            .Where(s => s.Id == MonitoringSettings.SingletonId)
            .Select(s => new DashboardSettingsDto(s.DashboardEventHours))
            .FirstOrDefaultAsync(ct) ?? DashboardSettingsDto.Default);

    private static async Task<Results<Ok<DashboardSettingsDto>, ProblemHttpResult>> UpdateDashboardAsync(
        DashboardSettingsDto dto, VedettaVipDbContext db, CancellationToken ct)
    {
        var settings = await db.MonitoringSettings.FirstAsync(s => s.Id == MonitoringSettings.SingletonId, ct);
        settings.DashboardEventHours = dto.RecentEventHours;
        if (await db.TrySaveChangesAsync(ct) is { } problem)
            return problem;
        return TypedResults.Ok(dto);
    }

    /// <summary>Percorsi di WinBox scritti negli installer del gestore winbox:// (letti da tutti: la pagina WinBox li mostra).</summary>
    internal static async Task<WinBoxSettingsDto> LoadWinBoxAsync(VedettaVipDbContext db, CancellationToken ct) =>
        await db.MonitoringSettings.AsNoTracking()
            .Where(s => s.Id == MonitoringSettings.SingletonId)
            .Select(s => new WinBoxSettingsDto(s.WinBoxWindowsPath, s.WinBoxLinuxPath))
            .FirstOrDefaultAsync(ct)
        ?? WinBoxSettingsDto.Default;

    private static async Task<Ok<WinBoxSettingsDto>> GetWinBoxAsync(VedettaVipDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await LoadWinBoxAsync(db, ct));

    /// <summary>Vuoto = nessun predefinito (l'installer cerca WinBox e, se non lo trova, chiede il percorso).</summary>
    private static async Task<Results<Ok<WinBoxSettingsDto>, ValidationProblem, ProblemHttpResult>> UpdateWinBoxAsync(
        WinBoxSettingsDto dto, VedettaVipDbContext db, CancellationToken ct)
    {
        var windows = string.IsNullOrWhiteSpace(dto.WindowsPath) ? null : dto.WindowsPath.Trim();
        var linux = string.IsNullOrWhiteSpace(dto.LinuxPath) ? null : dto.LinuxPath.Trim();

        var errors = new Dictionary<string, string[]>();
        if (WinBoxHandlerScripts.Validate(windows, windows: true) is { } windowsError)
            errors[nameof(dto.WindowsPath)] = [windowsError];
        if (WinBoxHandlerScripts.Validate(linux, windows: false) is { } linuxError)
            errors[nameof(dto.LinuxPath)] = [linuxError];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var settings = await db.MonitoringSettings.FirstAsync(s => s.Id == MonitoringSettings.SingletonId, ct);
        settings.WinBoxWindowsPath = windows;
        settings.WinBoxLinuxPath = linux;
        if (await db.TrySaveChangesAsync(ct) is { } problem)
            return problem;
        return TypedResults.Ok(new WinBoxSettingsDto(windows, linux));
    }

    /// <summary>Aggiorna le soglie generali e avvisa gli agenti: i device senza valori specifici le usano subito.</summary>
    private static async Task<Results<Ok<DetectionThresholdsDto>, ProblemHttpResult>> UpdateDetectionAsync(
        DetectionThresholdsDto dto, VedettaVipDbContext db, AgentNotifier agents, CancellationToken ct)
    {
        var settings = await db.MonitoringSettings.FirstOrDefaultAsync(s => s.Id == MonitoringSettings.SingletonId, ct);
        if (settings is null)
        {
            settings = new MonitoringSettings();
            db.MonitoringSettings.Add(settings);
        }

        settings.DownAfterFailures = dto.DownAfterFailures;
        settings.UpAfterSuccesses = dto.UpAfterSuccesses;
        settings.SnmpDegradedAfterFailures = dto.SnmpDegradedAfterFailures;

        if (await db.TrySaveChangesAsync(ct) is { } saveProblem)
            return saveProblem;

        await agents.TargetsChangedAsync();
        return TypedResults.Ok(dto);
    }
}
