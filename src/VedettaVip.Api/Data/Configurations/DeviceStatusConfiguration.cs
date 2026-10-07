// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VedettaVip.Api.Data.Entities;

namespace VedettaVip.Api.Data.Configurations;

public sealed class DeviceStatusConfiguration : IEntityTypeConfiguration<DeviceStatus>
{
    public void Configure(EntityTypeBuilder<DeviceStatus> b)
    {
        b.ToTable("DeviceStatuses");
        b.HasKey(s => s.DeviceId);
        b.Property(s => s.AgentId).HasMaxLength(64);

        // Eccezione voluta alla regola "FK verso Device in RESTRICT": è stato derivato, lo storico è in Events
        b.HasOne(s => s.Device)
            .WithOne(d => d.Status)
            .HasForeignKey<DeviceStatus>(s => s.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
