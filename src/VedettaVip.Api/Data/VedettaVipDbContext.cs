// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using VedettaVip.Api.Data.Entities;
using VedettaVip.Shared.Contracts;
using NodeState = VedettaVip.Shared.Models.NodeState;

namespace VedettaVip.Api.Data;

/// <summary>
/// Contesto EF, con le tabelle di ASP.NET Core Identity (AspNetUsers, AspNetRoles…). Implementa
/// IDataProtectionKeyContext: le chiavi di Data Protection (che cifrano i segreti e i cookie di sessione) stanno nella
/// tabella DataProtectionKeys, così sopravvivono a riavvii e nuovi container.
/// </summary>
public sealed class VedettaVipDbContext(DbContextOptions<VedettaVipDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Map> Maps => Set<Map>();
    public DbSet<MapNode> MapNodes => Set<MapNode>();
    public DbSet<MapLink> MapLinks => Set<MapLink>();
    public DbSet<Event> Events => Set<Event>();
    public DbSet<DeviceStatus> DeviceStatuses => Set<DeviceStatus>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<DeviceInterface> DeviceInterfaces => Set<DeviceInterface>();
    public DbSet<MonitoringSettings> MonitoringSettings => Set<MonitoringSettings>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<NotificationSettings> NotificationSettings => Set<NotificationSettings>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<OpenThresholdAlert> OpenThresholdAlerts => Set<OpenThresholdAlert>();
    public DbSet<MaintenanceWindow> MaintenanceWindows => Set<MaintenanceWindow>();
    public DbSet<SnmpCredential> SnmpCredentials => Set<SnmpCredential>();
    public DbSet<RouterOsCredential> RouterOsCredentials => Set<RouterOsCredential>();
    public DbSet<RouterOsWatch> RouterOsWatches => Set<RouterOsWatch>();
    public DbSet<DeviceNeighbor> DeviceNeighbors => Set<DeviceNeighbor>();
    public DbSet<DiscoveryIgnore> DiscoveryIgnores => Set<DiscoveryIgnore>();
    public DbSet<DeviceArpEntry> DeviceArpEntries => Set<DeviceArpEntry>();
    public DbSet<DiscoveryScan> DiscoveryScans => Set<DiscoveryScan>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // tabelle Identity
        modelBuilder.Entity<AppUser>().Property(u => u.DisplayName).HasMaxLength(128);
        modelBuilder.Entity<IdentityRole<Guid>>().HasData(VedettaVip.Api.Security.Roles.Seed); // "Roles" qui è la DbSet di Identity

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(VedettaVipDbContext).Assembly);
        SeedData.Apply(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enum salvati come testo: leggibili da psql e stabili se si riordinano i valori
        configurationBuilder.Properties<DeviceType>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<SnmpVersion>().HaveConversion<string>().HaveMaxLength(8);
        configurationBuilder.Properties<MapNodeKind>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<EventSeverity>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<NodeState>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<InterfaceOperStatus>().HaveConversion<string>().HaveMaxLength(8);
        configurationBuilder.Properties<SubscriptionScope>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<AlertFilter>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<NotifyState>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<NotificationChannel>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<DeliveryStatus>().HaveConversion<string>().HaveMaxLength(16);
    }
}
