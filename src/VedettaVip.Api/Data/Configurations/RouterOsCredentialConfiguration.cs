// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VedettaVip.Api.Data.Entities;

namespace VedettaVip.Api.Data.Configurations;

public sealed class RouterOsCredentialConfiguration : IEntityTypeConfiguration<RouterOsCredential>
{
    public void Configure(EntityTypeBuilder<RouterOsCredential> b)
    {
        b.ToTable("RouterOsCredentials", t => t.HasCheckConstraint("CK_RouterOsCredentials_Port", "\"Port\" BETWEEN 1 AND 65535"));
        b.Property(c => c.Name).HasMaxLength(128);
        b.Property(c => c.Description).HasMaxLength(512);
        b.Property(c => c.Username).HasMaxLength(64);
        b.Property(c => c.PasswordProtected).HasMaxLength(2048);
        b.HasIndex(c => c.Name).IsUnique();

        // RESTRICT ovunque: un profilo in uso non si elimina (l'API risponde 409 con l'elenco degli utilizzi)
        b.HasMany<Device>().WithOne(d => d.RouterOsCredential).HasForeignKey(d => d.RouterOsCredentialId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany<Customer>().WithOne(c => c.RouterOsCredential).HasForeignKey(c => c.RouterOsCredentialId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany<MonitoringSettings>().WithOne().HasForeignKey(s => s.DefaultRouterOsCredentialId).OnDelete(DeleteBehavior.Restrict);
    }
}
