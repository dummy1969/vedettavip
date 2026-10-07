// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using VedettaVip.Api.Options;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Services;

/// <summary>
/// Ultimo campione di traffico per interfaccia (DeviceId, IfIndex), solo in memoria: è il dato live della mappa.
/// Lo storico andrà nella hypertable Metric (TimescaleDB). Con più istanze dell'API servirà un backplane.
/// </summary>
public sealed class TrafficCache(TimeProvider time, IOptions<AgentOptions> options)
{
    private readonly ConcurrentDictionary<(Guid DeviceId, int IfIndex), InterfaceTrafficDto> latest = new();

    /// <summary>Salva i campioni più recenti di quelli già noti; restituisce quelli effettivamente aggiornati.</summary>
    public IReadOnlyList<InterfaceTrafficDto> Update(IEnumerable<InterfaceTrafficDto> samples)
    {
        var updated = new List<InterfaceTrafficDto>();
        foreach (var sample in samples)
        {
            var key = (sample.DeviceId, sample.IfIndex);
            var stored = latest.AddOrUpdate(key, sample, (_, old) => sample.Time > old.Time ? sample : old);
            if (ReferenceEquals(stored, sample))
                updated.Add(sample);
        }
        return updated;
    }

    /// <summary>Campioni non più vecchi di Agent:TrafficStaleSeconds (età misurata con l'orologio dell'API).</summary>
    public IReadOnlyList<InterfaceTrafficDto> Current()
    {
        var after = time.GetUtcNow() - TimeSpan.FromSeconds(options.Value.TrafficStaleSeconds);
        PurgeOlderThan(after - TimeSpan.FromHours(1));
        return latest.Values.Where(s => s.Time >= after).ToList();
    }

    /// <summary>Elimina le interfacce non più misurate (link rimossi, device cancellati), per non crescere all'infinito.</summary>
    private void PurgeOlderThan(DateTimeOffset limit)
    {
        foreach (var (key, sample) in latest)
            if (sample.Time < limit)
                latest.TryRemove(key, out _);
    }
}
