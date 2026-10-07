// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VedettaVip.Api.Data.Entities;

namespace VedettaVip.Api.Data.Configurations;

public sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> b)
    {
        // Un evento riguarda un device oppure un agente
        b.ToTable("Events", t =>
            t.HasCheckConstraint("CK_Events_Subject", "\"DeviceId\" IS NOT NULL OR \"AgentId\" IS NOT NULL"));

        b.Property(e => e.Type).HasMaxLength(64);
        b.Property(e => e.Message).HasMaxLength(2048);
        b.Property(e => e.AgentId).HasMaxLength(64);
        b.Property(e => e.NotifyNote).HasMaxLength(512);
        b.Property(e => e.AcknowledgedBy).HasMaxLength(128);
        b.Property(e => e.AlertKey).HasMaxLength(64);

        b.HasIndex(e => new { e.DeviceId, e.Time })
            .IsDescending(false, true);

        // Lista degli eventi da gestire in NOC: piccolo e veloce
        b.HasIndex(e => e.Time)
            .HasDatabaseName("IX_Events_Unacknowledged")
            .HasFilter("NOT \"Acknowledged\"");

        // Coda del dispatcher: solo gli eventi ancora da notificare
        b.HasIndex(e => e.NotifyAfter)
            .HasDatabaseName("IX_Events_NotifyPending")
            .HasFilter("\"NotifyState\" = 'Pending'");

        b.HasOne(e => e.Device)
            .WithMany()
            .HasForeignKey(e => e.DeviceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
