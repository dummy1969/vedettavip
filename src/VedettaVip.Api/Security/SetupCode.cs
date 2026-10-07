// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Security.Cryptography;
using System.Text;

namespace VedettaVip.Api.Security;

/// <summary>
/// Codice monouso per creare il primo amministratore, scritto nel log all'avvio finché non esiste nessun utente
/// (come la password iniziale di Jenkins): chi raggiunge per primo un'installazione nuova non può prendersela
/// senza accesso ai log del server. Cambia a ogni avvio.
/// </summary>
public sealed class SetupCode
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // senza 0/O e 1/I

    public string Value { get; } = Generate();

    public bool Matches(string? candidate)
    {
        var normalized = (candidate ?? "").Trim().ToUpperInvariant().Replace("-", "").Replace(" ", "");
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalized)),
            SHA256.HashData(Encoding.UTF8.GetBytes(Value.Replace("-", ""))));
    }

    private static string Generate()
    {
        var chars = Enumerable.Range(0, 12).Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]).ToArray();
        return $"{new string(chars, 0, 4)}-{new string(chars, 4, 4)}-{new string(chars, 8, 4)}";
    }
}
