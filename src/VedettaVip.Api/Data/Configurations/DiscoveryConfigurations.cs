// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VedettaVip.Api.Data.Entities;

namespace VedettaVip.Api.Data.Configurations;

public sealed class DeviceNeighborConfiguration : IEntityTypeConfiguration<DeviceNeighbor>
{
    public void Configure(EntityTypeBuilder<DeviceNeighbor> b)
    {
        b.ToTable("DeviceNeighbors");
        b.Property(n => n.Protocol).HasMaxLength(32);
        b.Property(n => n.LocalInterface).HasMaxLength(128);
        b.Property(n => n.Identity).HasMaxLength(128);
        b.Property(n => n.Address).HasMaxLength(64);
        b.Property(n => n.MacAddress).HasMaxLength(32);
        b.Property(n => n.RemoteInterface).HasMaxLength(128);
        b.Property(n => n.Platform).HasMaxLength(128);
        b.Property(n => n.Version).HasMaxLength(64);
        b.Property(n => n.Board).HasMaxLength(64);
        b.Property(n => n.Capabilities).HasMaxLength(128);
        b.HasIndex(n => n.DeviceId);
        // Dato derivato, riletto dall'agente: si elimina con il device (come DeviceInterfaces)
        b.HasOne(n => n.Device).WithMany().HasForeignKey(n => n.DeviceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class DiscoveryIgnoreConfiguration : IEntityTypeConfiguration<DiscoveryIgnore>
{
    public void Configure(EntityTypeBuilder<DiscoveryIgnore> b)
    {
        b.ToTable("DiscoveryIgnores");
        b.HasKey(i => i.Key);
        b.Property(i => i.Key).HasMaxLength(512);
        b.Property(i => i.Label).HasMaxLength(256);
    }
}

public sealed class DeviceArpEntryConfiguration : IEntityTypeConfiguration<DeviceArpEntry>
{
    public void Configure(EntityTypeBuilder<DeviceArpEntry> b)
    {
        b.ToTable("DeviceArpEntries");
        b.Property(a => a.Address).HasMaxLength(64);
        b.Property(a => a.MacAddress).HasMaxLength(32);
        b.Property(a => a.Interface).HasMaxLength(128);
        b.Property(a => a.HostName).HasMaxLength(128);
        b.Property(a => a.Comment).HasMaxLength(256);
        b.HasIndex(a => a.DeviceId);
        b.HasOne(a => a.Device).WithMany().HasForeignKey(a => a.DeviceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class DiscoveryScanConfiguration : IEntityTypeConfiguration<DiscoveryScan>
{
    public void Configure(EntityTypeBuilder<DiscoveryScan> b)
    {
        b.ToTable("DiscoveryScans");
        b.Property(s => s.Cidr).HasMaxLength(32);
        b.Property(s => s.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(s => s.AgentId).HasMaxLength(64);
        b.Property(s => s.Error).HasMaxLength(512);
        b.Property(s => s.CreatedBy).HasMaxLength(128);
        b.HasIndex(s => s.CreatedAt);
        b.HasMany(s => s.Hosts).WithOne().HasForeignKey(h => h.ScanId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class DiscoveryScanHostConfiguration : IEntityTypeConfiguration<DiscoveryScanHost>
{
    public void Configure(EntityTypeBuilder<DiscoveryScanHost> b)
    {
        b.ToTable("DiscoveryScanHosts");
        b.Property(h => h.Address).HasMaxLength(64);
        b.Property(h => h.DnsName).HasMaxLength(255);
        b.Property(h => h.SysName).HasMaxLength(128);
        b.Property(h => h.SysDescr).HasMaxLength(512);
        b.Property(h => h.OpenPorts).HasMaxLength(128);
    }
}
