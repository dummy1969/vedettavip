// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using VedettaVip.Api.Data;
using VedettaVip.Api.Data.Entities;
using VedettaVip.Api.Security;
using VedettaVip.Api.Services;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Endpoints;

/// <summary>Finestre di manutenzione: lettura per tutti, gestione per Operatori e Admin (sono interventi pianificati).</summary>
public static class MaintenanceEndpoints
{
    public static IEndpointRouteBuilder MapMaintenanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/maintenance").WithTags("Maintenance");
        group.MapGet("/", GetAllAsync);
        group.MapPost("/", CreateAsync).RequireOperator();
        group.MapPut("/{id:guid}", UpdateAsync).RequireOperator();
        group.MapDelete("/{id:guid}", DeleteAsync).RequireOperator();
        return app;
    }

    private static async Task<Ok<List<MaintenanceWindowDto>>> GetAllAsync(
        VedettaVipDbContext db, NotificationSettingsService settings, TimeProvider time, CancellationToken ct)
    {
        var zone = settings.TimeZone(await settings.LoadAsync(db, ct));
        var now = time.GetUtcNow();
        var windows = await db.MaintenanceWindows.AsNoTracking()
            .Include(w => w.Customer).Include(w => w.Map).Include(w => w.Device)
            .OrderBy(w => w.Name)
            .ToListAsync(ct);
        return TypedResults.Ok(windows.Select(w => ToDto(w, now, zone)).ToList());
    }

    private static async Task<Results<Created<MaintenanceWindowDto>, ValidationProblem>> CreateAsync(
        MaintenanceWindowUpsertDto dto, VedettaVipDbContext db, NotificationSettingsService settings, TimeProvider time, CancellationToken ct)
    {
        if (await ValidateAsync(db, dto, ct) is { } errors)
            return TypedResults.ValidationProblem(errors);

        var window = new MaintenanceWindow { Id = Guid.CreateVersion7(), Name = "" };
        Apply(window, dto);
        db.MaintenanceWindows.Add(window);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/maintenance/{window.Id}", await LoadDtoAsync(db, settings, time, window.Id, ct));
    }

    private static async Task<Results<Ok<MaintenanceWindowDto>, NotFound, ValidationProblem>> UpdateAsync(
        Guid id, MaintenanceWindowUpsertDto dto, VedettaVipDbContext db, NotificationSettingsService settings, TimeProvider time, CancellationToken ct)
    {
        var window = await db.MaintenanceWindows.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (window is null)
            return TypedResults.NotFound();
        if (await ValidateAsync(db, dto, ct) is { } errors)
            return TypedResults.ValidationProblem(errors);

        Apply(window, dto);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await LoadDtoAsync(db, settings, time, id, ct));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteAsync(Guid id, VedettaVipDbContext db, CancellationToken ct)
    {
        var deleted = await db.MaintenanceWindows.Where(w => w.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static void Apply(MaintenanceWindow w, MaintenanceWindowUpsertDto dto)
    {
        w.Name = dto.Name.Trim();
        w.Scope = dto.Scope;
        w.CustomerId = dto.Scope == MaintenanceScope.Customer ? dto.CustomerId : null;
        w.MapId = dto.Scope == MaintenanceScope.Map ? dto.MapId : null;
        w.DeviceId = dto.Scope == MaintenanceScope.Device ? dto.DeviceId : null;
        w.Recurrence = dto.Recurrence;
        var once = dto.Recurrence == MaintenanceRecurrence.Once;
        w.StartsAt = once ? dto.StartsAt?.ToUniversalTime() : null;
        w.EndsAt = once ? dto.EndsAt?.ToUniversalTime() : null;
        w.DaysOfWeek = once ? 0 : dto.DaysOfWeek;
        w.StartTime = once ? null : dto.StartTime;
        w.DurationMinutes = once ? 0 : dto.DurationMinutes;
        w.Enabled = dto.Enabled;
        w.Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();
    }

    private static async Task<Dictionary<string, string[]>?> ValidateAsync(VedettaVipDbContext db, MaintenanceWindowUpsertDto dto, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(dto.Name))
            errors["Name"] = ["Il nome è obbligatorio."];

        var targetOk = dto.Scope switch
        {
            MaintenanceScope.All => true,
            MaintenanceScope.Customer => dto.CustomerId is { } c && await db.Customers.AnyAsync(x => x.Id == c, ct),
            MaintenanceScope.Map => dto.MapId is { } m && await db.Maps.AnyAsync(x => x.Id == m, ct),
            _ => dto.DeviceId is { } d && await db.Devices.AnyAsync(x => x.Id == d, ct)
        };
        if (!targetOk)
            errors["Scope"] = ["Scegliere il cliente, la mappa o il dispositivo della finestra."];

        if (dto.Recurrence == MaintenanceRecurrence.Once)
        {
            if (dto.StartsAt is not { } s || dto.EndsAt is not { } e || e <= s)
                errors["EndsAt"] = ["Indicare inizio e fine, con la fine successiva all'inizio."];
        }
        else
        {
            if (dto.DaysOfWeek is < 1 or > 127) errors["DaysOfWeek"] = ["Scegliere almeno un giorno."];
            if (dto.StartTime is null) errors["StartTime"] = ["Indicare l'ora di inizio."];
            if (dto.DurationMinutes < 1) errors["DurationMinutes"] = ["Indicare la durata."];
        }

        return errors.Count > 0 ? errors : null;
    }

    private static async Task<MaintenanceWindowDto> LoadDtoAsync(
        VedettaVipDbContext db, NotificationSettingsService settings, TimeProvider time, Guid id, CancellationToken ct)
    {
        var zone = settings.TimeZone(await settings.LoadAsync(db, ct));
        var w = await db.MaintenanceWindows.AsNoTracking()
            .Include(x => x.Customer).Include(x => x.Map).Include(x => x.Device)
            .FirstAsync(x => x.Id == id, ct);
        return ToDto(w, time.GetUtcNow(), zone);
    }

    private static MaintenanceWindowDto ToDto(MaintenanceWindow w, DateTimeOffset now, TimeZoneInfo zone)
    {
        var schedule = NotificationDispatcher.ToSchedule(w);
        return new MaintenanceWindowDto(
            w.Id, w.Name, w.Scope, w.CustomerId, w.MapId, w.DeviceId,
            w.Customer?.Name ?? w.Map?.Name ?? w.Device?.Name,
            w.Recurrence, w.StartsAt, w.EndsAt, w.DaysOfWeek, w.StartTime, w.DurationMinutes, w.Enabled, w.Notes,
            MaintenanceSchedule.IsActive(schedule, now, zone), MaintenanceSchedule.NextStart(schedule, now, zone));
    }
}
