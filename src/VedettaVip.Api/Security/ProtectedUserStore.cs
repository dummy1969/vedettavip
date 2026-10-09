// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using VedettaVip.Api.Data;
using VedettaVip.Api.Data.Entities;
using VedettaVip.Api.Services;

namespace VedettaVip.Api.Security;

/// <summary>
/// Store degli utenti che cifra con Data Protection i token interni di Identity (AspNetUserTokens con provider
/// "[AspNetUserStore]": chiave TOTP dell'app di autenticazione e codici di recupero), che Identity salverebbe in chiaro.
/// Con un dump del database non si possono generare codici validi. Un valore non decifrabile (chiavi di Data Protection
/// perse) vale come assente: la verifica in due passaggi va riattivata (un amministratore può azzerarla).
/// </summary>
public sealed class ProtectedUserStore(
    VedettaVipDbContext context, SecretProtector protector, ILogger<ProtectedUserStore> logger, IdentityErrorDescriber? describer = null)
    : UserStore<AppUser, IdentityRole<Guid>, VedettaVipDbContext, Guid>(context, describer)
{
    /// <summary>Provider dei token interni di UserStoreBase (costante privata di Identity).</summary>
    public const string InternalLoginProvider = "[AspNetUserStore]";
    public const string RecoveryCodesTokenName = "RecoveryCodes";

    public override Task SetTokenAsync(AppUser user, string loginProvider, string name, string? value, CancellationToken cancellationToken)
    {
        if (loginProvider == InternalLoginProvider && value is not null)
            value = protector.Protect(value);
        return base.SetTokenAsync(user, loginProvider, name, value, cancellationToken);
    }

    public override async Task<string?> GetTokenAsync(AppUser user, string loginProvider, string name, CancellationToken cancellationToken)
    {
        var value = await base.GetTokenAsync(user, loginProvider, name, cancellationToken);
        if (loginProvider != InternalLoginProvider || string.IsNullOrEmpty(value))
            return value;
        if (protector.TryUnprotect(value, out var plaintext))
            return plaintext;

        logger.LogError("Token {Name} dell'utente {UserName} non decifrabile: la verifica in due passaggi va azzerata e riattivata",
            name, user.UserName);
        return null;
    }
}
