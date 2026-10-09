// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace VedettaVip.Api.Services;

/// <summary>
/// Cifra i segreti salvati nel database (password SMTP, token Telegram) con ASP.NET Core Data Protection.
/// Le chiavi stanno nella tabella DataProtectionKeys (vedi Program.cs): senza di esse i segreti non sono leggibili.
/// </summary>
public sealed class SecretProtector(IDataProtectionProvider provider, ILogger<SecretProtector> logger)
{
    // "NetMap" è il nome storico del progetto: il purpose fa parte della chiave, cambiarlo renderebbe illeggibili i segreti salvati
    private readonly IDataProtector protector = provider.CreateProtector("NetMap.Secrets.v1");

    public string Protect(string plaintext) => protector.Protect(plaintext);

    /// <summary>Come <see cref="Unprotect"/>, senza log: per i chiamanti che danno un messaggio proprio.</summary>
    public bool TryUnprotect(string protectedValue, out string? plaintext)
    {
        try
        {
            plaintext = protector.Unprotect(protectedValue);
            return true;
        }
        catch (CryptographicException)
        {
            plaintext = null;
            return false;
        }
    }

    /// <summary>Null se assente o non decifrabile (chiavi perse o ruotate oltre la scadenza): va reinserito dalla UI.</summary>
    public string? Unprotect(string? protectedValue)
    {
        if (string.IsNullOrEmpty(protectedValue))
            return null;
        try
        {
            return protector.Unprotect(protectedValue);
        }
        catch (CryptographicException ex)
        {
            logger.LogError("Segreto non decifrabile ({Message}): reinserirlo da Impostazioni", ex.Message);
            return null;
        }
    }
}
