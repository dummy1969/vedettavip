// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VedettaVip.Api.Data.Entities;

namespace VedettaVip.Api.Data.Configurations;

public sealed class OpenThresholdAlertConfiguration : IEntityTypeConfiguration<OpenThresholdAlert>
{
    public void Configure(EntityTypeBuilder<OpenThresholdAlert> b)
    {
        b.ToTable("OpenThresholdAlerts");
        b.HasKey(a => new { a.Kind, a.DeviceId, a.LinkId });
        b.Property(a => a.Kind).HasMaxLength(32);
        // Stato derivato: sparisce con il device
        b.HasOne(a => a.Device).WithMany().HasForeignKey(a => a.DeviceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class MaintenanceWindowConfiguration : IEntityTypeConfiguration<MaintenanceWindow>
{
    public void Configure(EntityTypeBuilder<MaintenanceWindow> b)
    {
        b.ToTable("MaintenanceWindows", t =>
        {
            t.HasCheckConstraint("CK_MaintenanceWindows_Scope",
                """
                ("Scope" = 'All' AND "CustomerId" IS NULL AND "MapId" IS NULL AND "DeviceId" IS NULL) OR
                ("Scope" = 'Customer' AND "CustomerId" IS NOT NULL AND "MapId" IS NULL AND "DeviceId" IS NULL) OR
                ("Scope" = 'Map' AND "MapId" IS NOT NULL AND "CustomerId" IS NULL AND "DeviceId" IS NULL) OR
                ("Scope" = 'Device' AND "DeviceId" IS NOT NULL AND "CustomerId" IS NULL AND "MapId" IS NULL)
                """);
            t.HasCheckConstraint("CK_MaintenanceWindows_Schedule",
                """
                ("Recurrence" = 'Once' AND "StartsAt" IS NOT NULL AND "EndsAt" IS NOT NULL AND "EndsAt" > "StartsAt") OR
                ("Recurrence" = 'Weekly' AND "StartTime" IS NOT NULL AND "DaysOfWeek" BETWEEN 1 AND 127
                 AND "DurationMinutes" BETWEEN 1 AND 10080)
                """);
        });
        b.Property(w => w.Name).HasMaxLength(128);
        b.Property(w => w.Notes).HasMaxLength(1024);
        b.Property(w => w.Scope).HasConversion<string>().HasMaxLength(16);
        b.Property(w => w.Recurrence).HasConversion<string>().HasMaxLength(16);

        // Eliminare cliente, mappa o device elimina le finestre su di essi
        b.HasOne(w => w.Customer).WithMany().HasForeignKey(w => w.CustomerId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(w => w.Map).WithMany().HasForeignKey(w => w.MapId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(w => w.Device).WithMany().HasForeignKey(w => w.DeviceId).OnDelete(DeleteBehavior.Cascade);
    }
}
