// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.IO.Compression;
using System.Text.RegularExpressions;

namespace VedettaVip.Api.Services;

/// <summary>
/// Produttore dal MAC con i registri pubblici IEEE (MA-L 24 bit, MA-M 28, MA-S 36; risorsa oui.tsv.gz, vedi
/// third-party/ieee-oui): vince il prefisso più lungo. I MAC "amministrati localmente" (secondo bit del primo byte,
/// es. gli indirizzi Wi-Fi casuali di smartphone e PC) non hanno produttore.
/// </summary>
public static partial class MacVendors
{
    private static readonly Lazy<Dictionary<string, string>> Table = new(Load);

    public const string PrivateMac = "MAC privato (casuale)";

    /// <summary>Nome breve del produttore ("Yealink", "VMware", "MikroTik"); <see cref="PrivateMac"/> o null.</summary>
    public static string? Lookup(string? mac)
    {
        var hex = Hex(mac);
        if (hex is null)
            return null;
        if ((Convert.ToByte(hex[..2], 16) & 0x02) != 0)
            return PrivateMac;
        foreach (var length in (int[])[9, 7, 6])
            if (Table.Value.TryGetValue(hex[..length], out var org))
                return ShortName(org);
        return null;
    }

    /// <summary>12 cifre esadecimali maiuscole da "aa:bb:cc:dd:ee:ff", "AA-BB-..." o "aabb.ccdd.eeff"; null se non è un MAC.</summary>
    private static string? Hex(string? mac)
    {
        if (mac is null)
            return null;
        var hex = new string(mac.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();
        return hex.Length == 12 ? hex : null;
    }

    private static Dictionary<string, string> Load()
    {
        using var stream = typeof(MacVendors).Assembly.GetManifestResourceStream("VedettaVip.Api.oui.tsv.gz")
                           ?? throw new InvalidOperationException("Risorsa oui.tsv.gz mancante");
        using var reader = new StreamReader(new GZipStream(stream, CompressionMode.Decompress));
        var table = new Dictionary<string, string>(60_000);
        while (reader.ReadLine() is { } line)
            if (line.IndexOf('\t') is var tab and > 0)
                table[line[..tab]] = line[(tab + 1)..];
        return table;
    }

    // Marchi noti con ragioni sociali poco leggibili
    private static readonly (string Contains, string Brand)[] Brands =
    [
        ("Routerboard", "MikroTik"), ("MikroTik", "MikroTik"), ("Hewlett Packard", "HP"), ("HP Inc", "HP"),
        ("Hangzhou Hikvision", "Hikvision"), ("Zhejiang Dahua", "Dahua"), ("Super Micro", "Supermicro"),
        ("Raspberry Pi", "Raspberry Pi"), ("Ubiquiti", "Ubiquiti"), ("TP-LINK", "TP-Link"), ("Tp-Link", "TP-Link"),
        ("AVM Audiovisuelles", "AVM (Fritz!Box)"), ("ASUSTek", "ASUS"), ("Micro-Star", "MSI"), ("Intel Corporat", "Intel")
    ];

    [GeneratedRegex(@"\s*\(.*?\)")]
    private static partial Regex Parenthesis();

    [GeneratedRegex(@"[\s,.]*\b(co\.?,?\s*ltd|co\.?|ltd|limited|inc|incorporated|incorporation|corp|corporation|company|gmbh|ag|s\.?p\.?a|s\.?r\.?l|s\.?a|b\.?v|llc|plc|oy|ab|as|kg|technology|technologies|tech|network|networks|electronics|international|communications?|systems?|solutions|group|holdings?)\b\.?", RegexOptions.IgnoreCase)]
    private static partial Regex Suffix();

    /// <summary>"YEALINK(XIAMEN) NETWORK TECHNOLOGY CO.,LTD." → "Yealink"; "VMware, Inc." → "VMware".</summary>
    public static string ShortName(string organization)
    {
        foreach (var (contains, brand) in Brands)
            if (organization.Contains(contains, StringComparison.OrdinalIgnoreCase))
                return brand;

        var name = Parenthesis().Replace(organization, " ");
        for (var previous = ""; previous != name;)
        {
            previous = name;
            name = Suffix().Replace(name, "").Trim(' ', ',', '.', '-');
        }
        if (name.Length == 0)
            name = organization;
        // Parole TUTTE MAIUSCOLE → iniziale maiuscola ("KYOCERA" → "Kyocera"), salvo le sigle brevi (HP, AVM, ZTE)
        name = string.Join(' ', name.Split(' ').Select(w =>
            w.Length > 3 && w == w.ToUpperInvariant() && w.Any(char.IsLetter) ? w[0] + w[1..].ToLowerInvariant() : w));
        return name.Length > 40 ? name[..40] : name;
    }
}
