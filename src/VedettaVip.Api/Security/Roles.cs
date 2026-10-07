// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Identity;

namespace VedettaVip.Api.Security;

/// <summary>
/// Ruoli e policy. Viewer: consultazione (monitor NOC). Operator: modifica mappe e dispositivi, prende in carico gli
/// eventi. Admin: in più clienti, contatti, impostazioni e utenti. Ogni utente ha esattamente un ruolo.
/// </summary>
public static class Roles
{
    public const string Admin = VedettaVip.Shared.Contracts.UserRoles.Admin;
    public const string Operator = VedettaVip.Shared.Contracts.UserRoles.Operator;
    public const string Viewer = VedettaVip.Shared.Contracts.UserRoles.Viewer;

    // Id fissi: i ruoli sono creati dalla migration (HasData)
    public static readonly IdentityRole<Guid>[] Seed =
    [
        new() { Id = Guid.Parse("0198f000-0000-7000-8000-00000000a001"), Name = Admin, NormalizedName = "ADMIN", ConcurrencyStamp = "admin" },
        new() { Id = Guid.Parse("0198f000-0000-7000-8000-00000000a002"), Name = Operator, NormalizedName = "OPERATOR", ConcurrencyStamp = "operator" },
        new() { Id = Guid.Parse("0198f000-0000-7000-8000-00000000a003"), Name = Viewer, NormalizedName = "VIEWER", ConcurrencyStamp = "viewer" }
    ];
}

public static class Policies
{
    /// <summary>Admin o Operator.</summary>
    public const string Operator = "Operator";
    public const string Admin = "Admin";
}

public static class AuthorizationExtensions
{
    /// <summary>Scrittura su mappe, dispositivi, eventi: Admin o Operator.</summary>
    public static TBuilder RequireOperator<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(Policies.Operator);

    /// <summary>Gestione di clienti, contatti, impostazioni, utenti: solo Admin.</summary>
    public static TBuilder RequireAdmin<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(Policies.Admin);
}
