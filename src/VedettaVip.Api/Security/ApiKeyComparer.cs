// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Security.Cryptography;
using System.Text;

namespace VedettaVip.Api.Security;

/// <summary>
/// Confronto della chiave a tempo costante: si confrontano gli hash SHA-256 (lunghezza fissa) con
/// FixedTimeEquals, così né il contenuto né la lunghezza della chiave fornita influenzano i tempi.
/// </summary>
public static class ApiKeyComparer
{
    public static byte[] Hash(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));

    public static bool Matches(string? provided, ReadOnlySpan<byte> expectedHash)
    {
        Span<byte> providedHash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(provided ?? ""), providedHash);
        return CryptographicOperations.FixedTimeEquals(providedHash, expectedHash);
    }
}
