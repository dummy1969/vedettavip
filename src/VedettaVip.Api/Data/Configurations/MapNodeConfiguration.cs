// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VedettaVip.Api.Data.Entities;

namespace VedettaVip.Api.Data.Configurations;

public sealed class MapNodeConfiguration : IEntityTypeConfiguration<MapNode>
{
    public void Configure(EntityTypeBuilder<MapNode> b)
    {
        b.ToTable("MapNodes", t =>
        {
            // Coerenza tra Kind e riferimenti: Device ⇒ solo DeviceId, Submap ⇒ solo SubmapId, Static ⇒ nessuno
            t.HasCheckConstraint("CK_MapNodes_Kind",
                """
                ("Kind" = 'Device' AND "DeviceId" IS NOT NULL AND "SubmapId" IS NULL) OR
                ("Kind" = 'Submap' AND "SubmapId" IS NOT NULL AND "DeviceId" IS NULL) OR
                ("Kind" = 'Static' AND "DeviceId" IS NULL AND "SubmapId" IS NULL)
                """);
            t.HasCheckConstraint("CK_MapNodes_SubmapNotSelf", "\"SubmapId\" IS NULL OR \"SubmapId\" <> \"MapId\"");
        });

        b.Property(n => n.LabelTemplate).HasMaxLength(512);
        b.Property(n => n.Icon).HasMaxLength(64);

        // Un dispositivo compare al massimo una volta per mappa
        b.HasIndex(n => new { n.MapId, n.DeviceId })
            .IsUnique()
            .HasFilter("\"DeviceId\" IS NOT NULL");

        // Una sottomappa ha un solo nodo che la rappresenta: è ciò che rende univoco Map.ParentMapId
        b.HasIndex(n => n.SubmapId)
            .IsUnique()
            .HasFilter("\"SubmapId\" IS NOT NULL");

        b.HasOne(n => n.Map)
            .WithMany(m => m.Nodes)
            .HasForeignKey(n => n.MapId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(n => n.Submap)
            .WithMany()
            .HasForeignKey(n => n.SubmapId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(n => n.Device)
            .WithMany()
            .HasForeignKey(n => n.DeviceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
