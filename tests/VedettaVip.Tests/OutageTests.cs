// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.EntityFrameworkCore;
using VedettaVip.Api.Data;
using VedettaVip.Api.Data.Entities;
using VedettaVip.Api.Services;
using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;

namespace VedettaVip.Tests;

/// <summary>Storico dei down: apertura, chiusura e riallineamento delle interruzioni (EF InMemory), disponibilità.</summary>
public class OutageTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid DeviceA = Guid.CreateVersion7(), DeviceB = Guid.CreateVersion7();

    [Fact]
    public async Task Device_state_opens_and_closes_ping_and_snmp()
    {
        await using var db = await CreateAsync();
        var book = await OutageBook.LoadAsync(db, [DeviceA], OutageBook.StateServices, default);

        book.SyncState(DeviceA, NodeState.Down, T0);
        book.SyncState(DeviceA, NodeState.Down, T0.AddSeconds(30)); // snapshot: nessun doppione
        book.SyncState(DeviceA, NodeState.Partial, T0.AddMinutes(5)); // il ping torna, SNMP no
        book.SyncState(DeviceA, NodeState.Unknown, T0.AddMinutes(6)); // non decide nulla
        book.SyncState(DeviceA, NodeState.Up, T0.AddMinutes(8));
        await db.SaveChangesAsync();

        var outages = await db.Outages.OrderBy(o => o.StartedAt).ToListAsync();
        Assert.Equal(2, outages.Count);
        Assert.Equal((OutageService.Ping, T0, T0.AddMinutes(5)), (outages[0].Service, outages[0].StartedAt, outages[0].EndedAt!.Value));
        Assert.Equal((OutageService.Snmp, T0.AddMinutes(5), T0.AddMinutes(8)), (outages[1].Service, outages[1].StartedAt, outages[1].EndedAt!.Value));
        Assert.All(outages, o => Assert.Null(o.EndNote));
    }

    [Fact]
    public async Task Down_up_down_in_one_report_leaves_one_open_outage()
    {
        await using var db = await CreateAsync();
        var book = await OutageBook.LoadAsync(db, [DeviceA], OutageBook.StateServices, default);
        book.SyncState(DeviceA, NodeState.Down, T0);
        book.SyncState(DeviceA, NodeState.Up, T0.AddMinutes(1));
        book.SyncState(DeviceA, NodeState.Down, T0.AddMinutes(2));
        await db.SaveChangesAsync();

        var outages = await db.Outages.OrderBy(o => o.StartedAt).ToListAsync();
        Assert.Equal(2, outages.Count);
        Assert.NotNull(outages[0].EndedAt);
        Assert.Null(outages[1].EndedAt);

        // Il report successivo ritrova l'interruzione aperta e la chiude
        var next = await OutageBook.LoadAsync(db, [DeviceA], OutageBook.StateServices, default);
        Assert.True(next.IsOpen(DeviceA, OutageBook.PingKey));
        next.SyncState(DeviceA, NodeState.Up, T0.AddMinutes(4));
        await db.SaveChangesAsync();
        Assert.Equal(0, await db.Outages.CountAsync(o => o.EndedAt == null));
    }

    [Fact]
    public async Task Load_reads_only_the_requested_services_and_devices()
    {
        await using var db = await CreateAsync();
        var book = await OutageBook.LoadAsync(db, null, OutageBook.StateServices, default);
        book.SyncState(DeviceA, NodeState.Down, T0);
        book.Open(DeviceA, OutageService.Latency, "latency", "latenza media 150 ms, soglia 100 ms", T0);
        book.Open(DeviceB, OutageService.Interface, "if:ether1", "interfaccia ether1 giù", T0);
        await db.SaveChangesAsync();

        var state = await OutageBook.LoadAsync(db, [DeviceA], OutageBook.StateServices, default);
        Assert.Equal([OutageBook.PingKey], state.OpenOutages.Select(o => o.Key));
        var thresholds = await OutageBook.LoadAsync(db, null, OutageBook.ThresholdServices, default);
        Assert.Equal(["latency"], thresholds.OpenOutages.Select(o => o.Key));
        var watches = await OutageBook.LoadAsync(db, [DeviceA], OutageBook.WatchServices, default);
        Assert.Empty(watches.OpenOutages);
    }

    [Fact]
    public async Task Close_appends_detail_and_never_ends_before_the_start()
    {
        await using var db = await CreateAsync();
        var book = await OutageBook.LoadAsync(db, null, OutageBook.ThresholdServices, default);
        Assert.False(book.Close(DeviceA, "latency", T0)); // niente di aperto

        book.Open(DeviceA, OutageService.Latency, "latency", "latenza media 150 ms", T0);
        Assert.True(book.Close(DeviceA, "latency", T0.AddSeconds(-5), detailSuffix: "picco 180 ms"));
        await db.SaveChangesAsync();

        var outage = await db.Outages.SingleAsync();
        Assert.Equal(T0, outage.EndedAt);
        Assert.Equal("latenza media 150 ms; picco 180 ms", outage.Detail);
    }

    [Fact]
    public async Task Maintenance_is_tagged_on_opened_and_closed_outages_only()
    {
        await using var db = await CreateAsync();
        var book = await OutageBook.LoadAsync(db, null, OutageBook.StateServices, default);
        var loads = 0;
        Task<Func<Guid, string?>> Load(CancellationToken _)
        {
            loads++;
            return Task.FromResult<Func<Guid, string?>>(id => id == DeviceA ? "Aggiornamento firmware" : null);
        }

        await book.TagMaintenanceAsync(Load, default);
        Assert.Equal(0, loads); // nessuna interruzione toccata: finestre non caricate

        book.SyncState(DeviceA, NodeState.Down, T0);
        book.SyncState(DeviceB, NodeState.Down, T0);
        await book.TagMaintenanceAsync(Load, default);
        await db.SaveChangesAsync();
        Assert.Equal(1, loads);
        Assert.Equal("Aggiornamento firmware", (await db.Outages.SingleAsync(o => o.DeviceId == DeviceA)).Maintenance);
        Assert.Null((await db.Outages.SingleAsync(o => o.DeviceId == DeviceB)).Maintenance);
    }

    [Fact]
    public async Task Disabling_a_device_closes_its_open_outages()
    {
        await using var db = await CreateAsync();
        var book = await OutageBook.LoadAsync(db, null, OutageBook.StateServices, default);
        book.SyncState(DeviceA, NodeState.Down, T0);
        book.SyncState(DeviceB, NodeState.Down, T0);
        await db.SaveChangesAsync();

        (await db.Devices.SingleAsync(d => d.Id == DeviceA)).Enabled = false;
        (await db.Devices.SingleAsync(d => d.Id == DeviceB)).Name = "rinominato"; // modificato ma ancora abilitato
        await OutageBook.CloseForDisabledDevicesAsync(db, T0.AddHours(1), default);
        await db.SaveChangesAsync();

        var a = await db.Outages.SingleAsync(o => o.DeviceId == DeviceA);
        Assert.Equal((T0.AddHours(1), OutageBook.DisabledNote), (a.EndedAt!.Value, a.EndNote!));
        Assert.Null((await db.Outages.SingleAsync(o => o.DeviceId == DeviceB)).EndedAt);
    }

    [Fact]
    public void Overlap_is_clipped_to_the_period_and_open_outages_run_to_its_end()
    {
        DateTimeOffset from = T0, to = T0.AddDays(1);
        Assert.Equal(TimeSpan.FromHours(1), OutageStats.Overlap(T0.AddHours(-2), T0.AddHours(1), from, to));
        Assert.Equal(TimeSpan.FromHours(4), OutageStats.Overlap(T0.AddHours(20), null, from, to));
        Assert.Equal(TimeSpan.FromMinutes(10), OutageStats.Overlap(T0.AddHours(3), T0.AddHours(3).AddMinutes(10), from, to));
        Assert.Equal(TimeSpan.Zero, OutageStats.Overlap(T0.AddDays(-3), T0.AddDays(-2), from, to));
    }

    [Fact]
    public void Availability_is_the_share_of_the_period_without_downtime()
    {
        DateTimeOffset from = T0, to = T0.AddDays(10);
        Assert.Equal(100, OutageStats.AvailabilityPct(TimeSpan.Zero, from, to));
        Assert.Equal(99, OutageStats.AvailabilityPct(TimeSpan.FromHours(2.4), from, to), 6);
        Assert.Equal(0, OutageStats.AvailabilityPct(TimeSpan.FromDays(20), from, to));
        Assert.Equal(100, OutageStats.AvailabilityPct(TimeSpan.FromHours(1), to, from)); // periodo vuoto
    }

    private static async Task<VedettaVipDbContext> CreateAsync()
    {
        var db = new VedettaVipDbContext(new DbContextOptionsBuilder<VedettaVipDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Devices.Add(new Device { Id = DeviceA, Name = "A", Address = "192.0.2.1", Enabled = true });
        db.Devices.Add(new Device { Id = DeviceB, Name = "B", Address = "192.0.2.2", Enabled = true });
        await db.SaveChangesAsync();
        return db;
    }
}
