// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VedettaVip.Api.Data.Entities;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Data.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> b)
    {
        b.ToTable("Customers");
        b.Property(c => c.Name).HasMaxLength(128);
        b.Property(c => c.Notes).HasMaxLength(1024);
        b.HasIndex(c => c.Name).IsUnique();
    }
}

public sealed class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> b)
    {
        b.ToTable("Contacts", t =>
            t.HasCheckConstraint("CK_Contacts_Channel", "\"Email\" IS NOT NULL OR \"TelegramChatId\" IS NOT NULL"));
        b.Property(c => c.Name).HasMaxLength(128);
        b.Property(c => c.Email).HasMaxLength(256);
        b.Property(c => c.TelegramChatId).HasMaxLength(64);
    }
}

public sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> b)
    {
        b.ToTable("Subscriptions", t => t.HasCheckConstraint("CK_Subscriptions_Scope",
            """
            ("Scope" = 'All' AND "CustomerId" IS NULL AND "MapId" IS NULL) OR
            ("Scope" = 'Customer' AND "CustomerId" IS NOT NULL AND "MapId" IS NULL) OR
            ("Scope" = 'Map' AND "MapId" IS NOT NULL AND "CustomerId" IS NULL)
            """));

        // Eliminare contatto, cliente o mappa elimina le iscrizioni relative
        b.HasOne(s => s.Contact).WithMany(c => c.Subscriptions).HasForeignKey(s => s.ContactId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(s => s.Customer).WithMany().HasForeignKey(s => s.CustomerId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(s => s.Map).WithMany().HasForeignKey(s => s.MapId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class NotificationSettingsConfiguration : IEntityTypeConfiguration<NotificationSettings>
{
    public void Configure(EntityTypeBuilder<NotificationSettings> b)
    {
        b.ToTable("NotificationSettings", t =>
        {
            t.HasCheckConstraint("CK_NotificationSettings_Singleton", $"\"Id\" = {NotificationSettings.SingletonId}");
            t.HasCheckConstraint("CK_NotificationSettings_Port", "\"SmtpPort\" BETWEEN 1 AND 65535");
            t.HasCheckConstraint("CK_NotificationSettings_Delay", "\"DelaySeconds\" BETWEEN 0 AND 3600");
            t.HasCheckConstraint("CK_NotificationSettings_Reminder", "\"ReminderMinutes\" BETWEEN 0 AND 10080");
        });
        b.Property(s => s.Id).ValueGeneratedNever();
        b.Property(s => s.SmtpHost).HasMaxLength(255);
        b.Property(s => s.SmtpUsername).HasMaxLength(256);
        b.Property(s => s.SmtpPasswordProtected).HasMaxLength(2048);
        b.Property(s => s.SmtpFromAddress).HasMaxLength(256);
        b.Property(s => s.SmtpFromName).HasMaxLength(128);
        b.Property(s => s.TelegramBotTokenProtected).HasMaxLength(2048);
        b.Property(s => s.TimeZoneId).HasMaxLength(64);
        b.Property(s => s.PublicUrl).HasMaxLength(512);
        b.Property(s => s.SmtpSecurity).HasConversion<string>().HasMaxLength(16);

        // Riga sempre presente, canali disattivati finché non vengono configurati dalla UI
        b.HasData(new NotificationSettings
        {
            Id = NotificationSettings.SingletonId,
            SmtpPort = 587,
            SmtpSecurity = SmtpSecurity.StartTls,
            SmtpValidateCertificate = true,
            DelaySeconds = 60,
            TimeZoneId = "Europe/Rome"
        });
    }
}

public sealed class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> b)
    {
        b.ToTable("NotificationDeliveries");
        b.Property(d => d.ContactName).HasMaxLength(128);
        b.Property(d => d.Destination).HasMaxLength(256);
        b.Property(d => d.LastError).HasMaxLength(1024);

        b.HasOne(d => d.Event).WithMany(e => e.Deliveries).HasForeignKey(d => d.EventId).OnDelete(DeleteBehavior.Cascade);
        // Lo storico degli invii resta anche se il contatto viene eliminato (nome e destinazione sono copiati)
        b.HasOne<Contact>().WithMany().HasForeignKey(d => d.ContactId).OnDelete(DeleteBehavior.SetNull);

        // Coda dei tentativi
        b.HasIndex(d => d.NextAttemptAt)
            .HasDatabaseName("IX_NotificationDeliveries_Pending")
            .HasFilter("\"Status\" = 'Pending'");
    }
}
