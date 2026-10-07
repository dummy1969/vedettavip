// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VedettaVip.Api.Data.Entities;

namespace VedettaVip.Api.Data.Configurations;

public sealed class SnmpCredentialConfiguration : IEntityTypeConfiguration<SnmpCredential>
{
    public void Configure(EntityTypeBuilder<SnmpCredential> b)
    {
        b.ToTable("SnmpCredentials");
        b.Property(c => c.Name).HasMaxLength(128);
        b.Property(c => c.Description).HasMaxLength(512);
        b.Property(c => c.CommunityProtected).HasMaxLength(2048);
        b.HasIndex(c => c.Name).IsUnique();

        // RESTRICT ovunque: un profilo in uso non si elimina (l'API risponde 409 con l'elenco degli utilizzi)
        b.HasMany<Device>().WithOne(d => d.SnmpCredential).HasForeignKey(d => d.SnmpCredentialId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany<Customer>().WithOne(c => c.SnmpCredential).HasForeignKey(c => c.SnmpCredentialId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany<MonitoringSettings>().WithOne().HasForeignKey(s => s.DefaultSnmpCredentialId).OnDelete(DeleteBehavior.Restrict);
    }
}
