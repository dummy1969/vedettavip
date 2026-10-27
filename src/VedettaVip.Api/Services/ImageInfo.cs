// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Buffers.Binary;

namespace VedettaVip.Api.Services;

/// <summary>
/// Riconosce le immagini di sfondo dal contenuto (non dall'estensione né dal Content-Type dichiarato) e ne legge le
/// dimensioni dall'intestazione, senza decodificarle: PNG (IHDR), JPEG (marker SOF), WebP (VP8, VP8L, VP8X). Tutto il resto,
/// SVG compreso (può contenere script), è rifiutato.
/// </summary>
public static class ImageInfo
{
    /// <summary>Lato massimo accettato (un'immagine più grande pesa troppo nel browser).</summary>
    public const int MaxSide = 16_384;

    public static bool TryRead(ReadOnlySpan<byte> data, out string contentType, out int width, out int height)
    {
        (contentType, width, height) = ("", 0, 0);
        var ok = TryPng(data, ref width, ref height) ? Set(ref contentType, "image/png")
            : TryJpeg(data, ref width, ref height) ? Set(ref contentType, "image/jpeg")
            : TryWebP(data, ref width, ref height) && Set(ref contentType, "image/webp");
        return ok && width is > 0 and <= MaxSide && height is > 0 and <= MaxSide;
    }

    private static bool Set(ref string target, string value)
    {
        target = value;
        return true;
    }

    private static bool TryPng(ReadOnlySpan<byte> d, ref int w, ref int h)
    {
        ReadOnlySpan<byte> signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
        if (d.Length < 24 || !d[..8].SequenceEqual(signature) || !d.Slice(12, 4).SequenceEqual("IHDR"u8))
            return false;
        w = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(d.Slice(16, 4)), int.MaxValue);
        h = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(d.Slice(20, 4)), int.MaxValue);
        return true;
    }

    /// <summary>Scorre i segmenti fino al primo SOF (C0–CF tranne C4 DHT, C8 JPG, CC DAC).</summary>
    private static bool TryJpeg(ReadOnlySpan<byte> d, ref int w, ref int h)
    {
        if (d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8)
            return false;
        var i = 2;
        while (i + 4 <= d.Length)
        {
            if (d[i] != 0xFF)
                return false;
            var marker = d[i + 1];
            if (marker == 0xFF) { i++; continue; }                        // riempimento
            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7)) { i += 2; continue; } // senza lunghezza
            if (marker == 0xD9 || marker == 0xDA)
                return false;                                             // fine o dati prima di un SOF
            var length = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(i + 2, 2));
            if (length < 2)
                return false;
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                if (i + 9 > d.Length)
                    return false;
                h = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(i + 5, 2));
                w = BinaryPrimitives.ReadUInt16BigEndian(d.Slice(i + 7, 2));
                return true;
            }
            i += 2 + length;
        }
        return false;
    }

    private static bool TryWebP(ReadOnlySpan<byte> d, ref int w, ref int h)
    {
        if (d.Length < 30 || !d[..4].SequenceEqual("RIFF"u8) || !d.Slice(8, 4).SequenceEqual("WEBP"u8))
            return false;
        var chunk = d.Slice(12, 4);
        var body = d[20..];
        if (chunk.SequenceEqual("VP8 "u8))
        {
            // Frame chiave: 3 byte di intestazione, firma 9D 01 2A, poi larghezza e altezza a 14 bit
            if (body.Length < 10 || body[3] != 0x9D || body[4] != 0x01 || body[5] != 0x2A)
                return false;
            w = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(6, 2)) & 0x3FFF;
            h = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(8, 2)) & 0x3FFF;
            return true;
        }
        if (chunk.SequenceEqual("VP8L"u8))
        {
            if (body.Length < 5 || body[0] != 0x2F)
                return false;
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(body.Slice(1, 4));
            w = (int)(bits & 0x3FFF) + 1;
            h = (int)((bits >> 14) & 0x3FFF) + 1;
            return true;
        }
        if (chunk.SequenceEqual("VP8X"u8))
        {
            if (body.Length < 10)
                return false;
            w = (body[4] | body[5] << 8 | body[6] << 16) + 1;
            h = (body[7] | body[8] << 8 | body[9] << 16) + 1;
            return true;
        }
        return false;
    }
}
