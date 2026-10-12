// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VedettaVip.Api.Data.Entities;

namespace VedettaVip.Api.Data.Configurations;

public sealed class OutageConfiguration : IEntityTypeConfiguration<Outage>
{
    public void Configure(EntityTypeBuilder<Outage> b)
    {
        b.ToTable("Outages", t =>
            t.HasCheckConstraint("CK_Outages_Interval", "\"EndedAt\" IS NULL OR \"EndedAt\" >= \"StartedAt\""));

        b.Property(o => o.Key).HasMaxLength(128);
        b.Property(o => o.Detail).HasMaxLength(256);
        b.Property(o => o.EndNote).HasMaxLength(128);
        b.Property(o => o.Maintenance).HasMaxLength(128);

        // Scheda del device: le più recenti per prime
        b.HasIndex(o => new { o.DeviceId, o.StartedAt }).IsDescending(false, true);
        // Vista globale (tutti i device, per periodo)
        b.HasIndex(o => o.StartedAt).IsDescending();
        // Una sola interruzione aperta per ciò che è giù; è anche l'indice delle interruzioni in corso
        b.HasIndex(o => new { o.DeviceId, o.Key })
            .HasDatabaseName("IX_Outages_Open")
            .IsUnique()
            .HasFilter("\"EndedAt\" IS NULL");

        // Storico derivato come DeviceStatus: si elimina con il device (gli eventi restano RESTRICT)
        b.HasOne(o => o.Device).WithMany().HasForeignKey(o => o.DeviceId).OnDelete(DeleteBehavior.Cascade);
    }
}
