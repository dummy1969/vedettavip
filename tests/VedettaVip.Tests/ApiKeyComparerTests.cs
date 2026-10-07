// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Api.Security;

namespace VedettaVip.Tests;

public class ApiKeyComparerTests
{
    private const string Key = "0123456789abcdef0123456789abcdef-chiave-di-test"; // gitleaks:allow (chiave fittizia dei test)
    private static readonly byte[] Expected = ApiKeyComparer.Hash(Key);

    [Fact]
    public void Matches_the_correct_key() =>
        Assert.True(ApiKeyComparer.Matches(Key, Expected));

    [Theory]
    [InlineData("0123456789abcdef0123456789abcdef-chiave-di-tesT")] // stessa lunghezza, ultimo carattere diverso
    [InlineData("0123456789abcdef")]                                 // più corta (prefisso)
    [InlineData("0123456789abcdef0123456789abcdef-chiave-di-test-e-altro")] // più lunga
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_wrong_keys(string? provided) =>
        Assert.False(ApiKeyComparer.Matches(provided, Expected));
}
