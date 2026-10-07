// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VedettaVip.Api.Data.Entities;

namespace VedettaVip.Api.Data.Configurations;

public sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> b)
    {
        b.ToTable("Devices", t =>
        {
            t.HasCheckConstraint("CK_Devices_ParentNotSelf", "\"ParentDeviceId\" IS NULL OR \"ParentDeviceId\" <> \"Id\"");
            t.HasCheckConstraint("CK_Devices_DownAfterFailures", "\"DownAfterFailures\" IS NULL OR \"DownAfterFailures\" BETWEEN 1 AND 100");
            t.HasCheckConstraint("CK_Devices_UpAfterSuccesses", "\"UpAfterSuccesses\" IS NULL OR \"UpAfterSuccesses\" BETWEEN 1 AND 100");
            t.HasCheckConstraint("CK_Devices_SnmpDegradedAfterFailures",
                "\"SnmpDegradedAfterFailures\" IS NULL OR \"SnmpDegradedAfterFailures\" BETWEEN 1 AND 100");
            t.HasCheckConstraint("CK_Devices_CpuThresholdPct", "\"CpuThresholdPct\" IS NULL OR \"CpuThresholdPct\" BETWEEN 0 AND 100");
            t.HasCheckConstraint("CK_Devices_TemperatureThresholdC", "\"TemperatureThresholdC\" IS NULL OR \"TemperatureThresholdC\" BETWEEN 0 AND 150");
        });

        b.Property(d => d.Name).HasMaxLength(128);
        b.Property(d => d.Address).HasMaxLength(255);
        b.Property(d => d.Icon).HasMaxLength(64);

        b.HasIndex(d => d.Name);

        // Eliminare un cliente lascia i device senza cliente
        b.HasOne(d => d.Customer)
            .WithMany()
            .HasForeignKey(d => d.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);

        b.HasOne(d => d.ParentDevice)
            .WithMany()
            .HasForeignKey(d => d.ParentDeviceId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
