// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Identity;

namespace VedettaVip.Api.Data.Entities;

/// <summary>Utente di VedettaVip (ASP.NET Core Identity, tabelle AspNet*). Un solo ruolo per utente: Admin, Operator o Viewer.</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public required string DisplayName { get; set; }
    /// <summary>Utente disattivato: non può accedere e le sue sessioni scadono entro un minuto (security stamp).</summary>
    public bool Disabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
}
