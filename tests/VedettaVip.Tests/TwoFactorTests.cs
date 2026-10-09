// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VedettaVip.Api.Data;
using VedettaVip.Api.Data.Entities;
using VedettaVip.Api.Security;
using VedettaVip.Api.Services;

namespace VedettaVip.Tests;

/// <summary>Verifica in due passaggi: UserManager di Identity con lo store cifrato (EF InMemory) e codici TOTP calcolati qui.</summary>
public sealed class TwoFactorTests : IAsyncLifetime
{
    private ServiceProvider services = null!;
    private AsyncServiceScope scope;
    private UserManager<AppUser> users = null!;
    private VedettaVipDbContext db = null!;
    private AppUser user = null!;

    public async Task InitializeAsync()
    {
        var collection = new ServiceCollection();
        collection.AddLogging();
        collection.AddDataProtection().UseEphemeralDataProtectionProvider();
        collection.AddSingleton<SecretProtector>();
        var dbName = Guid.NewGuid().ToString();
        collection.AddDbContext<VedettaVipDbContext>(o => o.UseInMemoryDatabase(dbName));
        collection.AddIdentityCore<AppUser>()
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<VedettaVipDbContext>()
            .AddUserStore<ProtectedUserStore>()
            .AddDefaultTokenProviders();
        services = collection.BuildServiceProvider();
        scope = services.CreateAsyncScope();
        users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        db = scope.ServiceProvider.GetRequiredService<VedettaVipDbContext>();

        user = new AppUser { UserName = "mario", DisplayName = "Mario" };
        Assert.True((await users.CreateAsync(user, "Password-di-prova-1")).Succeeded);
    }

    public async Task DisposeAsync()
    {
        await scope.DisposeAsync();
        await services.DisposeAsync();
    }

    [Fact]
    public void Store_is_the_protected_one() =>
        Assert.IsType<ProtectedUserStore>(scope.ServiceProvider.GetRequiredService<IUserStore<AppUser>>());

    [Fact]
    public async Task Authenticator_key_is_encrypted_in_the_database()
    {
        await users.ResetAuthenticatorKeyAsync(user);
        var key = await users.GetAuthenticatorKeyAsync(user);

        Assert.NotNull(key);
        Assert.Matches("^[A-Z2-7]{32}$", key);
        var stored = await RawTokenAsync("AuthenticatorKey");
        Assert.NotNull(stored);
        Assert.DoesNotContain(key, stored);
    }

    [Fact]
    public async Task Current_totp_code_is_accepted_and_a_wrong_one_is_not()
    {
        await users.ResetAuthenticatorKeyAsync(user);
        var key = (await users.GetAuthenticatorKeyAsync(user))!;
        var code = Totp(key, DateTimeOffset.UtcNow);
        var wrong = ((int.Parse(code) + 1) % 1_000_000).ToString("D6");

        Assert.True(await Verify(code));
        Assert.True(await Verify(code[..3] + " " + code[3..])); // digitato con lo spazio come lo mostrano le app
        Assert.False(await Verify(wrong));
        Assert.False(await Verify(Totp(key, DateTimeOffset.UtcNow.AddMinutes(-10)))); // codice vecchio
    }

    [Fact]
    public async Task Recovery_codes_are_encrypted_and_single_use()
    {
        var codes = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))!.ToList();

        Assert.Equal(10, codes.Count);
        Assert.All(codes, c => Assert.Matches("^[A-Z0-9]{5}-[A-Z0-9]{5}$", c));
        Assert.Equal(10, await users.CountRecoveryCodesAsync(user));
        var stored = await RawTokenAsync(ProtectedUserStore.RecoveryCodesTokenName);
        Assert.NotNull(stored);
        Assert.All(codes, c => Assert.DoesNotContain(c, stored));

        var typed = " " + codes[3].ToLowerInvariant() + " ";
        Assert.True((await users.RedeemTwoFactorRecoveryCodeAsync(user, TwoFactorText.NormalizeRecoveryCode(typed))).Succeeded);
        Assert.False((await users.RedeemTwoFactorRecoveryCodeAsync(user, codes[3])).Succeeded);
        Assert.Equal(9, await users.CountRecoveryCodesAsync(user));
    }

    [Fact]
    public async Task Undecryptable_key_counts_as_missing()
    {
        await users.ResetAuthenticatorKeyAsync(user);
        var token = await db.UserTokens.SingleAsync(t => t.UserId == user.Id && t.Name == "AuthenticatorKey");
        token.Value = "JBSWY3DPEHPK3PXP"; // in chiaro o cifrato con chiavi perse
        await db.SaveChangesAsync();

        Assert.Null(await users.GetAuthenticatorKeyAsync(user));
    }

    [Fact]
    public void Key_is_shown_in_groups_of_four() =>
        Assert.Equal("abcd efgh ijkl mn", TwoFactorText.FormatKey("ABCDEFGHIJKLMN"));

    [Fact]
    public void Authenticator_uri_escapes_the_user_name() =>
        Assert.Equal("otpauth://totp/VedettaVip:mario%40example.org?secret=ABCD&issuer=VedettaVip&digits=6",
            TwoFactorText.AuthenticatorUri("mario@example.org", "ABCD"));

    [Theory]
    [InlineData("123456", "123456")]
    [InlineData(" 123 456 ", "123456")]
    [InlineData("123-456", "123456")]
    public void Typed_code_is_normalized(string typed, string expected) =>
        Assert.Equal(expected, TwoFactorText.NormalizeCode(typed));

    [Fact]
    public void Qr_code_is_an_svg_data_uri()
    {
        var uri = TwoFactorText.QrCodeDataUri("otpauth://totp/VedettaVip:mario?secret=ABCD");

        Assert.StartsWith("data:image/svg+xml;base64,", uri);
        var svg = Encoding.UTF8.GetString(Convert.FromBase64String(uri["data:image/svg+xml;base64,".Length..]));
        Assert.Contains("<svg", svg);
    }

    private Task<bool> Verify(string code) =>
        users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, TwoFactorText.NormalizeCode(code));

    private async Task<string?> RawTokenAsync(string name) =>
        (await db.UserTokens.AsNoTracking().SingleOrDefaultAsync(t =>
            t.UserId == user.Id && t.LoginProvider == ProtectedUserStore.InternalLoginProvider && t.Name == name))?.Value;

    /// <summary>Codice TOTP come lo calcola un'app di autenticazione (RFC 6238: HMAC-SHA1, passo di 30 s, 6 cifre).</summary>
    private static string Totp(string base32Key, DateTimeOffset time)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, time.ToUnixTimeSeconds() / 30);
        var hash = HMACSHA1.HashData(Base32Decode(base32Key), counter);
        var offset = hash[^1] & 0x0F;
        var binary = BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset)) & 0x7FFFFFFF;
        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var output = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in input.TrimEnd('='))
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }
        return [.. output];
    }
}
