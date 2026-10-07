// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using VedettaVip.Shared.Contracts;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Pages;

/// <summary>Finestre di manutenzione: consultazione per tutti, gestione per Operatori e Admin.</summary>
public partial class Maintenance : IDisposable
{
    private static readonly (DayOfWeek Day, string Label)[] Days =
    [
        (DayOfWeek.Monday, "lun"), (DayOfWeek.Tuesday, "mar"), (DayOfWeek.Wednesday, "mer"), (DayOfWeek.Thursday, "gio"),
        (DayOfWeek.Friday, "ven"), (DayOfWeek.Saturday, "sab"), (DayOfWeek.Sunday, "dom")
    ];

    [Inject] private VedettaVipApiClient Api { get; set; } = default!;
    [Inject] private CurrentUser User { get; set; } = default!;

    /// <summary>Stato modificabile del form (date in ora locale del browser per datetime-local).</summary>
    private sealed class WindowForm
    {
        public string Name { get; set; } = "";
        public MaintenanceScope Scope { get; set; } = MaintenanceScope.Device;
        public string TargetId { get; set; } = "";
        public MaintenanceRecurrence Recurrence { get; set; } = MaintenanceRecurrence.Once;
        public DateTime? StartsAt { get; set; }
        public DateTime? EndsAt { get; set; }
        public int DaysOfWeek { get; set; }
        public TimeOnly? StartTime { get; set; } = new(2, 0);
        public int DurationMinutes { get; set; } = 120;
        public string? Notes { get; set; }
        public bool Enabled { get; set; } = true;

        public bool HasDay(DayOfWeek day) => (DaysOfWeek & (1 << (int)day)) != 0;
        public void ToggleDay(DayOfWeek day) => DaysOfWeek ^= 1 << (int)day;
    }

    private readonly CancellationTokenSource cts = new();
    private IReadOnlyList<MaintenanceWindowDto>? windows;
    private IReadOnlyList<DeviceDto> devices = [];
    private IReadOnlyList<CustomerDto> customers = [];
    private IReadOnlyList<MapSummaryDto> maps = [];
    private MaintenanceWindowDto? editing;
    private WindowForm? form;
    private bool busy;
    private string? error;

    protected override Task OnInitializedAsync() => RunAsync(async () =>
    {
        await User.GetAsync();
        devices = await Api.GetDevicesAsync(cts.Token);
        customers = await Api.GetCustomersAsync(cts.Token);
        maps = await Api.GetMapsAsync(cts.Token);
    });

    private void New()
    {
        var start = DateTime.Today.AddDays(1).AddHours(22);
        (editing, form) = (null, new WindowForm { StartsAt = start, EndsAt = start.AddHours(2) });
    }

    private void Edit(MaintenanceWindowDto w)
    {
        editing = w;
        form = new WindowForm
        {
            Name = w.Name, Scope = w.Scope, TargetId = (w.CustomerId ?? w.MapId ?? w.DeviceId)?.ToString() ?? "",
            Recurrence = w.Recurrence, StartsAt = w.StartsAt?.LocalDateTime, EndsAt = w.EndsAt?.LocalDateTime,
            DaysOfWeek = w.DaysOfWeek, StartTime = w.StartTime ?? new TimeOnly(2, 0), DurationMinutes = w.DurationMinutes == 0 ? 120 : w.DurationMinutes,
            Notes = w.Notes, Enabled = w.Enabled
        };
    }

    private void Close() => (editing, form) = (null, null);

    private Task SaveAsync() => RunAsync(async () =>
    {
        if (form is null) return;
        Guid? target = Guid.TryParse(form.TargetId, out var t) ? t : null;
        var dto = new MaintenanceWindowUpsertDto(
            form.Name.Trim(), form.Scope,
            form.Scope == MaintenanceScope.Customer ? target : null,
            form.Scope == MaintenanceScope.Map ? target : null,
            form.Scope == MaintenanceScope.Device ? target : null,
            form.Recurrence,
            form.StartsAt is { } s ? new DateTimeOffset(s) : null,  // ora locale del browser
            form.EndsAt is { } e ? new DateTimeOffset(e) : null,
            form.DaysOfWeek, form.StartTime, form.DurationMinutes, form.Enabled, form.Notes);

        if (editing is null)
            await Api.CreateMaintenanceWindowAsync(dto, cts.Token);
        else
            await Api.UpdateMaintenanceWindowAsync(editing.Id, dto, cts.Token);
        Close();
    });

    private Task DeleteAsync(MaintenanceWindowDto w) => RunAsync(async () =>
    {
        await Api.DeleteMaintenanceWindowAsync(w.Id, cts.Token);
        if (editing?.Id == w.Id) Close();
    });

    private async Task RunAsync(Func<Task> action)
    {
        busy = true;
        error = null;
        try
        {
            await action();
            windows = await Api.GetMaintenanceWindowsAsync(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (HttpRequestException ex)
        {
            error = ex.Message;
        }
        finally
        {
            busy = false;
        }
    }

    private IEnumerable<(Guid Id, string Name)> Targets(MaintenanceScope scope) => scope switch
    {
        MaintenanceScope.Customer => customers.Select(c => (c.Id, c.Name)),
        MaintenanceScope.Map => maps.Select(m => (m.Id, m.Name)),
        _ => devices.Select(d => (d.Id, $"{d.Name} ({d.Address})"))
    };

    private static string ScopeText(MaintenanceWindowDto w) => w.Scope switch
    {
        MaintenanceScope.All => "tutto",
        MaintenanceScope.Customer => $"cliente {w.ScopeName}",
        MaintenanceScope.Map => $"mappa {w.ScopeName}",
        _ => w.ScopeName ?? "?"
    };

    private static string ScheduleText(MaintenanceWindowDto w)
    {
        if (w.Recurrence == MaintenanceRecurrence.Once)
            return $"{w.StartsAt?.ToLocalTime():dd/MM/yyyy HH:mm} → {w.EndsAt?.ToLocalTime():dd/MM/yyyy HH:mm}";

        var days = string.Join(" ", Days.Where(d => (w.DaysOfWeek & (1 << (int)d.Day)) != 0).Select(d => d.Label));
        var duration = w.DurationMinutes % 60 == 0 ? $"{w.DurationMinutes / 60} h" : $"{w.DurationMinutes} min";
        return $"ogni {days}, {w.StartTime:HH\\:mm} per {duration}";
    }

    public void Dispose()
    {
        cts.Cancel();
        cts.Dispose();
    }
}
