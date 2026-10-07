// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VedettaVip.Api.Data.Entities;

namespace VedettaVip.Api.Data.Configurations;

public sealed class MapLinkConfiguration : IEntityTypeConfiguration<MapLink>
{
    public void Configure(EntityTypeBuilder<MapLink> b)
    {
        b.ToTable("MapLinks", t =>
        {
            t.HasCheckConstraint("CK_MapLinks_NotLoop", "\"FromNodeId\" <> \"ToNodeId\"");
            t.HasCheckConstraint("CK_MapLinks_IfIndexNeedsDevice", "\"IfIndex\" IS NULL OR \"DeviceId\" IS NOT NULL");
            t.HasCheckConstraint("CK_MapLinks_SpeedBps", "\"SpeedBps\" > 0");
        });

        b.HasOne(l => l.Map)
            .WithMany(m => m.Links)
            .HasForeignKey(l => l.MapId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(l => l.FromNode)
            .WithMany()
            .HasForeignKey(l => l.FromNodeId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(l => l.ToNode)
            .WithMany()
            .HasForeignKey(l => l.ToNodeId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(l => l.Device)
            .WithMany()
            .HasForeignKey(l => l.DeviceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
