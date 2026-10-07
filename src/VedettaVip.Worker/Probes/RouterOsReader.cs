// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Globalization;
using System.Text.RegularExpressions;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Worker.Probes;

/// <summary>
/// Legge risorse e salute di un router: /system/resource/print e /system/health/print. Le funzioni di conversione
/// sono pure (coperte da test): v6 restituisce la salute come attributi di una sola riga, v7 una riga per sensore
/// (name, value, type).
/// </summary>
public static partial class RouterOsReader
{
    public static async Task<RouterOsSampleDto> ReadAsync(RouterOsApiClient api, Guid deviceId, DateTimeOffset time, CancellationToken ct)
    {
        var resource = (await api.RunAsync(["/system/resource/print"], ct)).Rows.FirstOrDefault() ?? new Dictionary<string, string>();
        IReadOnlyList<IReadOnlyDictionary<string, string>> health;
        try
        {
            health = (await api.RunAsync(["/system/health/print"], ct)).Rows;
        }
        catch (RouterOsApiException)
        {
            health = []; // CHR, VM e alcuni modelli non hanno sensori: non è un errore del poll
        }

        var interfaces = (await api.RunAsync(["/interface/print", "=.proplist=name,type,running,disabled,comment"], ct)).Rows;
        IReadOnlyList<IReadOnlyDictionary<string, string>>? peers;
        try
        {
            peers = (await api.RunAsync(["/interface/wireguard/peers/print",
                "=.proplist=public-key,interface,name,comment,endpoint-address,endpoint-port,current-endpoint-address,current-endpoint-port,last-handshake,disabled"], ct)).Rows;
        }
        catch (RouterOsApiException)
        {
            peers = null; // RouterOS v6: niente WireGuard
        }

        return FromRows(deviceId, time, resource, health) with
        {
            Interfaces = Interfaces(interfaces),
            WireGuardPeers = peers is null ? null : WireGuardPeers(peers)
        };
    }

    public static List<RouterOsInterfaceDto> Interfaces(IReadOnlyList<IReadOnlyDictionary<string, string>> rows) =>
        rows.Where(r => r.ContainsKey("name"))
            .Select(r => new RouterOsInterfaceDto(Cut(r["name"], 128)!, Cut(r.GetValueOrDefault("type"), 64),
                Bool(r.GetValueOrDefault("running")), Bool(r.GetValueOrDefault("disabled")), Cut(r.GetValueOrDefault("comment"), 256)))
            .ToList();

    /// <summary>Peer WireGuard: l'endpoint attuale (current-endpoint) se c'è, altrimenti quello configurato.</summary>
    public static List<WireGuardPeerDto> WireGuardPeers(IReadOnlyList<IReadOnlyDictionary<string, string>> rows) =>
        rows.Where(r => r.ContainsKey("public-key"))
            .Select(r =>
            {
                var host = Blank(r.GetValueOrDefault("current-endpoint-address")) ?? Blank(r.GetValueOrDefault("endpoint-address"));
                var port = Blank(r.GetValueOrDefault("current-endpoint-port")) ?? Blank(r.GetValueOrDefault("endpoint-port"));
                return new WireGuardPeerDto(Cut(r["public-key"], 128)!, Cut(r.GetValueOrDefault("interface"), 128),
                    Cut(Blank(r.GetValueOrDefault("name")), 128), Cut(Blank(r.GetValueOrDefault("comment")), 256),
                    host is null ? null : Cut(port is null || port == "0" ? host : $"{host}:{port}", 128),
                    ParseUptime(r.GetValueOrDefault("last-handshake")), Bool(r.GetValueOrDefault("disabled")));
            })
            .ToList();

    private static bool Bool(string? s) => s is "true" or "yes";

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string? Cut(string? s, int max) => s is null ? null : s[..Math.Min(s.Length, max)];

    public static RouterOsSampleDto FromRows(
        Guid deviceId, DateTimeOffset time, IReadOnlyDictionary<string, string> resource, IReadOnlyList<IReadOnlyDictionary<string, string>> health)
    {
        var sensors = HealthValues(health);
        return new RouterOsSampleDto(
            deviceId, time, Ok: true,
            CpuLoadPct: Number(resource.GetValueOrDefault("cpu-load")),
            MemoryTotalBytes: Long(resource.GetValueOrDefault("total-memory")),
            MemoryFreeBytes: Long(resource.GetValueOrDefault("free-memory")),
            UptimeSeconds: ParseUptime(resource.GetValueOrDefault("uptime")),
            Version: Short(resource.GetValueOrDefault("version")),
            BoardName: Short(resource.GetValueOrDefault("board-name")),
            Architecture: Short(resource.GetValueOrDefault("architecture-name")),
            // CPU prima della scheda: è la più significativa per il carico; "temperature" sui modelli con un solo sensore
            TemperatureC: First(sensors, "cpu-temperature", "temperature", "board-temperature1", "board-temperature", "switch-temperature"),
            VoltageV: First(sensors, "voltage", "psu-voltage", "psu1-voltage"));
    }

    /// <summary>Sensori come nome → valore numerico, sia nel formato v6 (attributi) sia v7 (name/value).</summary>
    public static Dictionary<string, double> HealthValues(IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
    {
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (row.TryGetValue("name", out var name) && row.TryGetValue("value", out var value))
            {
                if (Number(value) is { } v) values[name] = v;
            }
            else
            {
                foreach (var (key, raw) in row)
                    if (key != ".id" && Number(raw) is { } v)
                        values[key] = v;
            }
        }
        return values;
    }

    [GeneratedRegex(@"(\d+)([wdhms])")]
    private static partial Regex UptimePart();

    [GeneratedRegex(@"(?:(\d+)d)?(\d{1,2}):(\d{2}):(\d{2})$")]
    private static partial Regex UptimeClock();

    /// <summary>"2w3d4h5m6s", "5h", "1d02:03:04" o "02:03:04" → secondi; null se non riconosciuto.</summary>
    public static long? ParseUptime(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        text = text.Trim();

        long seconds = 0;
        if (UptimeClock().Match(text) is { Success: true } clock)
        {
            if (clock.Groups[1].Success) seconds += long.Parse(clock.Groups[1].Value) * 86_400;
            seconds += long.Parse(clock.Groups[2].Value) * 3600 + long.Parse(clock.Groups[3].Value) * 60 + long.Parse(clock.Groups[4].Value);
            var weeks = Regex.Match(text, @"(\d+)w");
            return seconds + (weeks.Success ? long.Parse(weeks.Groups[1].Value) * 604_800 : 0);
        }

        var parts = UptimePart().Matches(text);
        if (parts.Count == 0 || string.Concat(parts.Select(p => p.Value)) != text)
            return null;
        foreach (Match p in parts)
            seconds += long.Parse(p.Groups[1].Value) * p.Groups[2].Value[0] switch
            {
                'w' => 604_800, 'd' => 86_400, 'h' => 3600, 'm' => 60, _ => 1
            };
        return seconds;
    }

    private static double? First(Dictionary<string, double> values, params string[] names) =>
        names.Select(n => values.TryGetValue(n, out var v) ? v : (double?)null).FirstOrDefault(v => v is not null);

    /// <summary>"45", "24.5", "45C", "12.3V", "7%" → numero (RouterOS a volte aggiunge l'unità).</summary>
    private static double? Number(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var trimmed = s.Trim().TrimEnd('C', 'V', '%', 'W', 'A', ' ');
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static long? Long(string? s) => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static string? Short(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim()[..Math.Min(s.Trim().Length, 64)];
}
