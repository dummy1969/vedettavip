// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VedettaVip.Api.Data.Entities;

namespace VedettaVip.Api.Data.Configurations;

public sealed class DeviceInterfaceConfiguration : IEntityTypeConfiguration<DeviceInterface>
{
    public void Configure(EntityTypeBuilder<DeviceInterface> b)
    {
        b.ToTable("DeviceInterfaces");
        b.HasKey(i => new { i.DeviceId, i.IfIndex });
        b.Property(i => i.Name).HasMaxLength(128);
        b.Property(i => i.Alias).HasMaxLength(256);

        // Eccezione motivata alla regola RESTRICT (come DeviceStatus): inventario derivato, riletto dall'agente
        b.HasOne(i => i.Device)
            .WithMany()
            .HasForeignKey(i => i.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
