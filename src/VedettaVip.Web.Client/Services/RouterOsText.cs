// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Globalization;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Web.Client.Services;

/// <summary>Testi delle letture RouterOS per tabella, tooltip e variabili d'etichetta della mappa.</summary>
public static class RouterOsText
{
    /// <summary>"3g 4h", "5h 12m", "8m".</summary>
    public static string Uptime(long? seconds)
    {
        if (seconds is not { } s) return "?";
        var t = TimeSpan.FromSeconds(s);
        return t.TotalDays >= 1 ? $"{(int)t.TotalDays}g {t.Hours}h"
            : t.TotalHours >= 1 ? $"{t.Hours}h {t.Minutes}m"
            : t.TotalMinutes >= 1 ? $"{t.Minutes}m"
            : $"{t.Seconds} s";
    }

    /// <summary>"CPU 12% · RAM 25% · 52 °C" (solo i valori presenti).</summary>
    public static string Summary(RouterOsSampleDto s)
    {
        var parts = new List<string>();
        if (s.CpuLoadPct is { } cpu) parts.Add($"CPU {cpu:0}%");
        if (s.MemoryUsedPct is { } mem) parts.Add($"RAM {mem:0}%");
        if (s.TemperatureC is { } t) parts.Add($"{t:0} °C");
        return string.Join(" · ", parts);
    }

    public static string Details(RouterOsSampleDto s) => string.Join(" · ", new[]
    {
        s.Version is { } v ? $"RouterOS {v}" : null,
        s.BoardName,
        s.UptimeSeconds is not null ? $"uptime {Uptime(s.UptimeSeconds)}" : null,
        s.VoltageV is { } volt ? $"{volt.ToString("0.0", CultureInfo.CurrentCulture)} V" : null,
        $"letto alle {s.Time.ToLocalTime():HH:mm:ss}" + (s.Service is { } svc ? $" via {svc}" : "")
    }.Where(x => x is not null));

    /// <summary>Tooltip di una lettura fallita: motivo, servizio e ora.</summary>
    public static string ErrorDetails(RouterOsSampleDto s) =>
        $"{s.Error}" + (s.Service is { } svc ? $" ({svc}" : " (") + $", {s.Time.ToLocalTime():HH:mm:ss})";

    /// <summary>Variabili d'etichetta del nodo: [Cpu] [Mem] [Temp] [Volt] [Uptime] [Version] [Board]; "?" se assenti.</summary>
    public static void Apply(IDictionary<string, string> values, RouterOsSampleDto? s)
    {
        values["Cpu"] = s?.CpuLoadPct is { } cpu ? cpu.ToString("0", CultureInfo.InvariantCulture) : "?";
        values["Mem"] = s?.MemoryUsedPct is { } mem ? mem.ToString("0", CultureInfo.InvariantCulture) : "?";
        values["Temp"] = s?.TemperatureC is { } t ? t.ToString("0", CultureInfo.InvariantCulture) : "?";
        values["Volt"] = s?.VoltageV is { } v ? v.ToString("0.0", CultureInfo.InvariantCulture) : "?";
        values["Uptime"] = s is { Ok: true } ? Uptime(s.UptimeSeconds) : "?";
        values["Version"] = s?.Version?.Split(' ')[0] ?? "?";
        values["Board"] = s?.BoardName ?? "?";
    }
}
