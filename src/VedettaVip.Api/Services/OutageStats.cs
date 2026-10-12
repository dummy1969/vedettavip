// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

namespace VedettaVip.Api.Services;

/// <summary>Calcoli sulle interruzioni (puri, coperti da test).</summary>
public static class OutageStats
{
    /// <summary>Durata di un'interruzione dentro il periodo [from, to]; quelle in corso arrivano fino a <paramref name="to"/>.</summary>
    public static TimeSpan Overlap(DateTimeOffset startedAt, DateTimeOffset? endedAt, DateTimeOffset from, DateTimeOffset to)
    {
        var start = startedAt > from ? startedAt : from;
        var end = endedAt is { } e && e < to ? e : to;
        return end > start ? end - start : TimeSpan.Zero;
    }

    /// <summary>
    /// Disponibilità in % sul periodo: il tempo giù in manutenzione non conta (è previsto). Le interruzioni dello stesso
    /// servizio di un device non si sovrappongono (una sola aperta per volta), quindi le durate si sommano.
    /// </summary>
    public static double AvailabilityPct(TimeSpan down, DateTimeOffset from, DateTimeOffset to)
    {
        var period = to - from;
        if (period <= TimeSpan.Zero)
            return 100;
        var pct = 100 * (1 - down / period);
        return Math.Clamp(pct, 0, 100);
    }
}
