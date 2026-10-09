// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Text;
using Net.Codecrete.QrCodeGenerator;

namespace VedettaVip.Api.Security;

/// <summary>Testi della verifica in due passaggi: chiave leggibile, URI otpauth:// per le app TOTP, QR code, codici digitati.</summary>
public static class TwoFactorText
{
    public const string Issuer = "VedettaVip";

    /// <summary>Chiave base32 a gruppi di 4 caratteri minuscoli, per chi la inserisce a mano nell'app.</summary>
    public static string FormatKey(string key)
    {
        var sb = new StringBuilder(key.Length + key.Length / 4);
        for (var i = 0; i < key.Length; i++)
        {
            if (i > 0 && i % 4 == 0)
                sb.Append(' ');
            sb.Append(char.ToLowerInvariant(key[i]));
        }
        return sb.ToString();
    }

    /// <summary>Formato Key URI di Google Authenticator, letto da tutte le app TOTP (SHA-1, 6 cifre, 30 s: i default di Identity).</summary>
    public static string AuthenticatorUri(string userName, string key) =>
        $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(userName)}" +
        $"?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6";

    /// <summary>QR code come data URI SVG (nero su bianco anche col tema scuro: le fotocamere lo leggono meglio).</summary>
    public static string QrCodeDataUri(string text)
    {
        var svg = QrCode.EncodeText(text, QrCode.Ecc.Medium).ToSvgString(4);
        return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
    }

    /// <summary>Codice TOTP digitato: senza spazi e trattini ("123 456", "123-456").</summary>
    public static string NormalizeCode(string code) =>
        string.Concat(code.Where(c => !char.IsWhiteSpace(c) && c != '-'));

    /// <summary>Codice di recupero digitato ("XXXXX-XXXXX", maiuscolo): senza spazi; il trattino fa parte del codice.</summary>
    public static string NormalizeRecoveryCode(string code) =>
        string.Concat(code.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
}
