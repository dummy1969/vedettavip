// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VedettaVip.Api.Data;
using VedettaVip.Api.Data.Entities;
using VedettaVip.Api.Hubs;
using VedettaVip.Api.Services;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Tests;

/// <summary>Messaggio "MapsChanged": mappe con le antenate, id della scheda che ha fatto la modifica.</summary>
public class MapNotifierTests
{
    private static readonly Guid Root = Guid.CreateVersion7(), Branch = Guid.CreateVersion7(), Leaf = Guid.CreateVersion7(),
        Other = Guid.CreateVersion7();

    private static readonly Dictionary<Guid, Guid?> Parents = new()
    {
        [Root] = null, [Branch] = Root, [Leaf] = Branch, [Other] = null
    };

    [Fact]
    public void Adds_all_ancestors_once()
    {
        var ids = MapNotifier.WithAncestors([Leaf, Branch], Parents);
        Assert.Equal(new[] { Root, Branch, Leaf }.Order(), ids.Order());
    }

    [Fact]
    public void Unknown_map_is_kept_and_a_cycle_does_not_loop()
    {
        var deleted = Guid.CreateVersion7(); // mappa appena eliminata: non c'è più nella tabella
        Assert.Equal([deleted], MapNotifier.WithAncestors([deleted], Parents));

        Guid a = Guid.CreateVersion7(), b = Guid.CreateVersion7();
        var cycle = new Dictionary<Guid, Guid?> { [a] = b, [b] = a };
        Assert.Equal(new[] { a, b }.Order(), MapNotifier.WithAncestors([a], cycle).Order());
    }

    [Theory]
    [InlineData("0b6f2d7c5f8e4c1e9a3b2c1d0e9f8a7b", true)]
    [InlineData("abc-123", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("<script>", false)]
    [InlineData("a b", false)]
    public void Client_id_must_be_short_and_plain(string? value, bool valid) =>
        Assert.Equal(valid, MapNotifier.IsValidClientId(value));

    [Fact]
    public void Client_id_longer_than_64_is_rejected() => Assert.False(MapNotifier.IsValidClientId(new string('a', 65)));

    [Fact]
    public async Task Sends_maps_with_ancestors_and_the_client_id_of_the_request()
    {
        var (notifier, hub, db) = await CreateAsync(clientId: "scheda-1");
        await notifier.MapsChangedAsync(db, [Leaf]);

        var dto = Assert.Single(hub.Sent);
        Assert.Equal(new[] { Root, Branch, Leaf }.Order(), dto.MapIds!.Order());
        Assert.Equal("scheda-1", dto.ClientId);
    }

    [Fact]
    public async Task Device_change_notifies_the_maps_where_it_is_a_node()
    {
        var (notifier, hub, db) = await CreateAsync(clientId: "<bad>");
        var device = new Device { Id = Guid.CreateVersion7(), Name = "sw1", Address = "192.0.2.10" };
        db.Devices.Add(device);
        db.MapNodes.Add(new MapNode
        {
            Id = Guid.CreateVersion7(), MapId = Branch, Kind = MapNodeKind.Device, DeviceId = device.Id,
            LabelTemplate = MapNode.DefaultLabelTemplate
        });
        await db.SaveChangesAsync();

        await notifier.DevicesChangedAsync(db, [device.Id]);

        var dto = Assert.Single(hub.Sent);
        Assert.Equal(new[] { Root, Branch }.Order(), dto.MapIds!.Order());
        Assert.Null(dto.ClientId); // header malformato: non finisce nel messaggio inviato a tutti
    }

    [Fact]
    public async Task Nothing_is_sent_without_maps_and_null_means_all()
    {
        var (notifier, hub, db) = await CreateAsync(clientId: null);
        await notifier.MapsChangedAsync(db, []);
        await notifier.DevicesChangedAsync(db, [Guid.CreateVersion7()]); // device su nessuna mappa
        Assert.Empty(hub.Sent);

        await notifier.AllMapsChangedAsync();
        Assert.Null(Assert.Single(hub.Sent).MapIds);
    }

    private static async Task<(MapNotifier, FakeHub, VedettaVipDbContext)> CreateAsync(string? clientId)
    {
        var (notifier, hub) = CreateNotifier(clientId);
        var db = new VedettaVipDbContext(new DbContextOptionsBuilder<VedettaVipDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        foreach (var (id, parent) in Parents)
            db.Maps.Add(new Map { Id = id, Name = id.ToString(), ParentMapId = parent });
        await db.SaveChangesAsync();
        return (notifier, hub, db);
    }

    private static (MapNotifier, FakeHub) CreateNotifier(string? clientId)
    {
        var context = new DefaultHttpContext();
        if (clientId is not null)
            context.Request.Headers[MapClientHeaders.ClientId] = clientId;
        var hub = new FakeHub();
        return (new MapNotifier(hub, new StaticAccessor(context), NullLogger<MapNotifier>.Instance), hub);
    }

    /// <summary>
    /// Contesto fisso: HttpContextAccessor lo tiene in un AsyncLocal, e un valore impostato dentro un metodo async non
    /// tornerebbe al test (in produzione lo imposta il server all'inizio della richiesta).
    /// </summary>
    private sealed class StaticAccessor(HttpContext context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get => context; set { } }
    }

    /// <summary>Registra i messaggi MapsChanged inviati a tutti i client.</summary>
    private sealed class FakeHub : IHubContext<StatusHub>
    {
        private readonly FakeClients clients = new();

        public List<MapsChangedDto> Sent => clients.Sent;
        public IHubClients Clients => clients;
        public IGroupManager Groups => throw new NotSupportedException();
    }

    private sealed class FakeClients : IHubClients, IClientProxy
    {
        public List<MapsChangedDto> Sent { get; } = [];

        public IClientProxy All => this;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Client(string connectionId) => throw new NotSupportedException();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy Group(string groupName) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IClientProxy User(string userId) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Assert.Equal(StatusHubMessages.MapsChanged, method);
            Sent.Add(Assert.IsType<MapsChangedDto>(Assert.Single(args)));
            return Task.CompletedTask;
        }
    }
}
