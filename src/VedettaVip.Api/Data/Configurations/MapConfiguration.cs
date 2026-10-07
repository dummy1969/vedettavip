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
        });

        b.Property(m => m.Name).HasMaxLength(128);
        b.Property(m => m.BackgroundImage).HasMaxLength(512);

        b.HasOne(m => m.ParentMap)
            .WithMany()
            .HasForeignKey(m => m.ParentMapId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
