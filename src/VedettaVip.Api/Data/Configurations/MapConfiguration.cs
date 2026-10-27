// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VedettaVip.Api.Data.Entities;

namespace VedettaVip.Api.Data.Configurations;

public sealed class MapConfiguration : IEntityTypeConfiguration<Map>
{
    public void Configure(EntityTypeBuilder<Map> b)
    {
        b.ToTable("Maps", t =>
        {
            t.HasCheckConstraint("CK_Maps_GridSize", "\"GridSize\" > 0");
            t.HasCheckConstraint("CK_Maps_ParentNotSelf", "\"ParentMapId\" IS NULL OR \"ParentMapId\" <> \"Id\"");
            t.HasCheckConstraint("CK_Maps_BackgroundOpacity", "\"BackgroundOpacityPct\" BETWEEN 10 AND 100");
            t.HasCheckConstraint("CK_Maps_BackgroundScale", "\"BackgroundScalePct\" BETWEEN 1 AND 1000");
            t.HasCheckConstraint("CK_Maps_Geo",
                "(\"GeoLatitude\" IS NULL AND \"GeoLongitude\" IS NULL AND \"GeoZoom\" IS NULL) OR " +
                "(\"GeoLatitude\" BETWEEN -85.0511 AND 85.0511 AND \"GeoLongitude\" BETWEEN -180 AND 180 AND \"GeoZoom\" BETWEEN 1 AND 19)");
            t.HasCheckConstraint("CK_Maps_GeoMode", "\"BackgroundMode\" <> 'Geo' OR \"GeoZoom\" IS NOT NULL");
        });

        b.Property(m => m.Name).HasMaxLength(128);
        b.Property(m => m.BackgroundImage).HasMaxLength(512);

        b.HasOne(m => m.Background)
            .WithOne()
            .HasForeignKey<MapBackground>(i => i.MapId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(m => m.ParentMap)
            .WithMany()
            .HasForeignKey(m => m.ParentMapId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class MapBackgroundConfiguration : IEntityTypeConfiguration<MapBackground>
{
    public void Configure(EntityTypeBuilder<MapBackground> b)
    {
        b.ToTable("MapBackgrounds", t =>
            t.HasCheckConstraint("CK_MapBackgrounds_Size", "\"Width\" > 0 AND \"Height\" > 0"));
        b.HasKey(i => i.MapId);
        b.Property(i => i.ContentType).HasMaxLength(32);
    }
}
