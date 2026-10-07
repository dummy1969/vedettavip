// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VedettaVip.Api.Data.Entities;

namespace VedettaVip.Api.Data.Configurations;

public sealed class RouterOsWatchConfiguration : IEntityTypeConfiguration<RouterOsWatch>
{
    public void Configure(EntityTypeBuilder<RouterOsWatch> b)
    {
        b.ToTable("RouterOsWatches");
        b.Property(w => w.Key).HasMaxLength(128);
        b.Property(w => w.Label).HasMaxLength(256);
        b.Property(w => w.Detail).HasMaxLength(256);
        b.HasIndex(w => new { w.DeviceId, w.Kind, w.Key }).IsUnique();
        // Configurazione del device, non storico: si elimina con lui (lo storico resta negli eventi)
        b.HasOne(w => w.Device).WithMany().HasForeignKey(w => w.DeviceId).OnDelete(DeleteBehavior.Cascade);
    }
}
