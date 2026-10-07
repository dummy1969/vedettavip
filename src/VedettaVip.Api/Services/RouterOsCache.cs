// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using VedettaVip.Api.Options;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Services;

/// <summary>
/// Ultima lettura RouterOS per device, solo in memoria (dato live di mappa e pagina Dispositivi; lo storico è in Metrics).
/// Con più istanze dell'API servirà un backplane, come per il traffico.
/// </summary>
public sealed class RouterOsCache(TimeProvider time, IOptions<AgentOptions> options)
{
    private readonly ConcurrentDictionary<Guid, RouterOsSampleDto> latest = new();
    private readonly ConcurrentDictionary<Guid, int> watchProblems = new();

    /// <summary>Salva le letture più recenti di quelle note; restituisce quelle effettivamente aggiornate.</summary>
    public IReadOnlyList<RouterOsSampleDto> Update(IEnumerable<RouterOsSampleDto> samples)
    {
        var updated = new List<RouterOsSampleDto>();
        foreach (var sample in samples)
        {
            var stored = latest.AddOrUpdate(sample.DeviceId, sample, (_, old) => sample.Time > old.Time ? sample : old);
            if (ReferenceEquals(stored, sample))
                updated.Add(sample);
        }
        return updated;
    }

    /// <summary>Elementi sorvegliati giù per device, calcolati dall'ultima valutazione.</summary>
    public void SetWatchProblems(IReadOnlyCollection<Guid> deviceIds, IReadOnlyDictionary<Guid, int> problems)
    {
        foreach (var id in deviceIds)
            watchProblems[id] = problems.GetValueOrDefault(id);
    }

    /// <summary>
    /// Letture non più vecchie di Agent:RouterOsStaleSeconds, <b>senza</b> interfacce e peer (servono solo al pannello del
    /// device: <see cref="Latest"/>), con il numero di elementi sorvegliati giù. Le letture molto vecchie vengono eliminate.
    /// </summary>
    public IReadOnlyList<RouterOsSampleDto> Current()
    {
        var after = time.GetUtcNow() - TimeSpan.FromSeconds(options.Value.RouterOsStaleSeconds);
        foreach (var (id, s) in latest)
            if (s.Time < after - TimeSpan.FromHours(1))
                latest.TryRemove(id, out _);
        return latest.Values.Where(s => s.Time >= after).Select(Summary).ToList();
    }

    /// <summary>Lettura per il browser: senza le liste (pesanti con centinaia di router), con i problemi sorvegliati.</summary>
    public RouterOsSampleDto Summary(RouterOsSampleDto s) =>
        s with { Interfaces = null, WireGuardPeers = null, WatchProblems = watchProblems.TryGetValue(s.DeviceId, out var n) ? n : null };

    /// <summary>Ultima lettura completa di un device (anche vecchia), per il pannello del dispositivo.</summary>
    public RouterOsSampleDto? Latest(Guid deviceId) => latest.GetValueOrDefault(deviceId);
}
