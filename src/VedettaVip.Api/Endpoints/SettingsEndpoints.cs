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
        settings.MapGet("/remote-access", GetRemoteAccessAsync);
        settings.MapPut("/remote-access", UpdateRemoteAccessAsync).RequireAdmin();
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

    /// <summary>
    /// Accesso remoto: percorsi di WinBox e PuTTY scritti negli installer del gestore dei link e porte generali (letti da
    /// tutti: servono alla pagina Gestore dei link e al pannello dei dispositivi).
    /// </summary>
    internal static async Task<RemoteAccessSettingsDto> LoadRemoteAccessAsync(VedettaVipDbContext db, CancellationToken ct) =>
        await db.MonitoringSettings.AsNoTracking()
            .Where(s => s.Id == MonitoringSettings.SingletonId)
            .Select(s => new RemoteAccessSettingsDto(s.WinBoxWindowsPath, s.WinBoxLinuxPath, s.WinBoxDefaultPort,
                s.PuttyWindowsPath, s.PuttyLinuxPath, s.HttpDefaultPort, s.HttpsDefaultPort, s.SshDefaultPort, s.TelnetDefaultPort))
            .FirstOrDefaultAsync(ct)
        ?? RemoteAccessSettingsDto.Default;

    private static async Task<Ok<RemoteAccessSettingsDto>> GetRemoteAccessAsync(VedettaVipDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await LoadRemoteAccessAsync(db, ct));

    /// <summary>Percorsi vuoti = nessun predefinito (l'installer cerca il programma e, se non lo trova, chiede il percorso).</summary>
    private static async Task<Results<Ok<RemoteAccessSettingsDto>, ValidationProblem, ProblemHttpResult>> UpdateRemoteAccessAsync(
        RemoteAccessSettingsDto dto, VedettaVipDbContext db, MapNotifier notifier, CancellationToken ct)
    {
        static string? Blank(string? path) => string.IsNullOrWhiteSpace(path) ? null : path.Trim();
        var clean = dto with
        {
            WinBoxWindowsPath = Blank(dto.WinBoxWindowsPath), WinBoxLinuxPath = Blank(dto.WinBoxLinuxPath),
            PuttyWindowsPath = Blank(dto.PuttyWindowsPath), PuttyLinuxPath = Blank(dto.PuttyLinuxPath)
        };

        var errors = new Dictionary<string, string[]>();
        void Check(string field, string? path, bool windows)
        {
            if (LinkHandlerScripts.Validate(path, windows) is { } error)
                errors[field] = [error];
        }
        Check(nameof(dto.WinBoxWindowsPath), clean.WinBoxWindowsPath, windows: true);
        Check(nameof(dto.WinBoxLinuxPath), clean.WinBoxLinuxPath, windows: false);
        Check(nameof(dto.PuttyWindowsPath), clean.PuttyWindowsPath, windows: true);
        Check(nameof(dto.PuttyLinuxPath), clean.PuttyLinuxPath, windows: false);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        var settings = await db.MonitoringSettings.FirstAsync(s => s.Id == MonitoringSettings.SingletonId, ct);
        var portsChanged = (settings.WinBoxDefaultPort, settings.HttpDefaultPort, settings.HttpsDefaultPort, settings.SshDefaultPort,
            settings.TelnetDefaultPort) != (clean.WinBoxPort, clean.HttpPort, clean.HttpsPort, clean.SshPort, clean.TelnetPort);
        settings.WinBoxWindowsPath = clean.WinBoxWindowsPath;
        settings.WinBoxLinuxPath = clean.WinBoxLinuxPath;
        settings.WinBoxDefaultPort = clean.WinBoxPort;
        settings.PuttyWindowsPath = clean.PuttyWindowsPath;
        settings.PuttyLinuxPath = clean.PuttyLinuxPath;
        settings.HttpDefaultPort = clean.HttpPort;
        settings.HttpsDefaultPort = clean.HttpsPort;
        settings.SshDefaultPort = clean.SshPort;
        settings.TelnetDefaultPort = clean.TelnetPort;
        if (await db.TrySaveChangesAsync(ct) is { } problem)
            return problem;
        // Le porte sono nei nodi delle mappe (menu del nodo): le mappe aperte si ricaricano
        if (portsChanged)
            await notifier.AllMapsChangedAsync();
        return TypedResults.Ok(clean);
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
