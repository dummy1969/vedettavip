// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.EntityFrameworkCore;
using VedettaVip.Api.Data.Entities;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Data;

/// <summary>
/// Seed della migration iniziale: riproduce la mappa del prototipo (pagina /map).
/// Gli Id sono fissi perché HasData deve generare migration deterministiche.
/// </summary>
internal static class SeedData
{
    public static readonly Guid MainMapId = new("0198f000-0000-7000-8000-000000000001");
    public static readonly Guid BranchNorthMapId = new("0198f000-0000-7000-8000-000000000002");

    public static readonly Guid CoreDeviceId = new("0198f000-0000-7000-8000-000000000101");
    public static readonly Guid Sw1DeviceId = new("0198f000-0000-7000-8000-000000000102");
    public static readonly Guid Sw2DeviceId = new("0198f000-0000-7000-8000-000000000103");
    public static readonly Guid ApDeviceId = new("0198f000-0000-7000-8000-000000000104");

    public static readonly Guid CoreNodeId = new("0198f000-0000-7000-8000-000000000201");
    public static readonly Guid Sw1NodeId = new("0198f000-0000-7000-8000-000000000202");
    public static readonly Guid Sw2NodeId = new("0198f000-0000-7000-8000-000000000203");
    public static readonly Guid ApNodeId = new("0198f000-0000-7000-8000-000000000204");
    public static readonly Guid BranchNorthNodeId = new("0198f000-0000-7000-8000-000000000205");
    public static readonly Guid InternetNodeId = new("0198f000-0000-7000-8000-000000000206");

    private const string DeviceLabel = "[Name]\n[Address]\nCPU: [Cpu]%";
    private const long Gbps = 1_000_000_000;

    public static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Map>().HasData(
            new Map { Id = MainMapId, Name = "Sede principale", GridSize = 20 },
            new Map { Id = BranchNorthMapId, Name = "Filiale Nord", ParentMapId = MainMapId, GridSize = 20 });

        modelBuilder.Entity<Device>().HasData(
            Device(CoreDeviceId, "CCR2004-CORE", "10.0.0.1", DeviceType.Router, parent: null),
            Device(Sw1DeviceId, "CRS326-SW1", "10.0.0.2", DeviceType.Switch, parent: CoreDeviceId),
            Device(Sw2DeviceId, "CRS326-SW2", "10.0.0.3", DeviceType.Switch, parent: CoreDeviceId),
            Device(ApDeviceId, "cAP-ax-Uffici", "10.0.10.5", DeviceType.AccessPoint, parent: Sw1DeviceId));

        modelBuilder.Entity<MapNode>().HasData(
            DeviceNode(CoreNodeId, CoreDeviceId, 400, 100),
            DeviceNode(Sw1NodeId, Sw1DeviceId, 200, 300),
            DeviceNode(Sw2NodeId, Sw2DeviceId, 600, 300),
            DeviceNode(ApNodeId, ApDeviceId, 200, 480),
            new MapNode
            {
                Id = BranchNorthNodeId, MapId = MainMapId, Kind = MapNodeKind.Submap, SubmapId = BranchNorthMapId,
                X = 640, Y = 480, LabelTemplate = "[Name]\n(sottomappa)"
            },
            new MapNode
            {
                Id = InternetNodeId, MapId = MainMapId, Kind = MapNodeKind.Static,
                X = 700, Y = 100, LabelTemplate = "Internet"
            });

        modelBuilder.Entity<MapLink>().HasData(
            Link("0198f000-0000-7000-8000-000000000301", CoreNodeId, Sw1NodeId, 10 * Gbps),
            Link("0198f000-0000-7000-8000-000000000302", CoreNodeId, Sw2NodeId, 10 * Gbps),
            Link("0198f000-0000-7000-8000-000000000303", Sw1NodeId, ApNodeId, 1 * Gbps),
            Link("0198f000-0000-7000-8000-000000000304", Sw2NodeId, BranchNorthNodeId, Gbps / 10),
            Link("0198f000-0000-7000-8000-000000000305", CoreNodeId, InternetNodeId, 1 * Gbps));
    }

    private static Device Device(Guid id, string name, string address, DeviceType type, Guid? parent) => new()
    {
        Id = id, Name = name, Address = address, Type = type, Vendor = DeviceVendor.MikroTik,
        SnmpVersion = SnmpVersion.V2c, RouterOsApiEnabled = true, ParentDeviceId = parent, Enabled = true
    };

    private static MapNode DeviceNode(Guid id, Guid deviceId, double x, double y) => new()
    {
        Id = id, MapId = MainMapId, Kind = MapNodeKind.Device, DeviceId = deviceId,
        X = x, Y = y, LabelTemplate = DeviceLabel
    };

    private static MapLink Link(string id, Guid from, Guid to, long speedBps) => new()
    {
        Id = new Guid(id), MapId = MainMapId, FromNodeId = from, ToNodeId = to, SpeedBps = speedBps
    };
}
