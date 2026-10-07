// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Collections.Concurrent;
using VedettaVip.Shared.Contracts;
using VedettaVip.Worker.Probes;

namespace VedettaVip.Worker.State;

/// <summary>
/// Calcola i bps dai contatori ifHC* di due poll consecutivi, usando come base dei tempi il sysUpTime del
/// device (preciso, indipendente dai ritardi dell'agente). Il campione viene scartato e la base ripartita se:
/// <list type="bullet">
/// <item>sysUpTime non è aumentato (riavvio del device o wrap dei TimeTicks dopo 497 giorni);</item>
/// <item>un contatore a 64 bit è diminuito (reset del contatore, es. interfaccia ricreata).</item>
/// </list>
/// Contatori a 32 bit (apparati senza ifXTable): un valore più basso è di norma un giro del contatore (2³² byte, ogni
/// ~6 min a 100 Mbit/s): il delta si corregge, ma solo se la velocità risultante è plausibile per l'interfaccia.
/// </summary>
public sealed class TrafficCalculator
{
    /// <summary>Sotto questo intervallo il delta è troppo rumoroso: si tiene la base precedente.</summary>
    private const uint MinTicks = 100; // 1 s

    private readonly ConcurrentDictionary<(Guid DeviceId, int IfIndex), Baseline> baselines = new();

    private const ulong Wrap32 = 1UL << 32;

    private sealed record Baseline(uint UptimeTicks, ulong InOctets, ulong OutOctets, bool Is32Bit);

    /// <summary>Restituisce il traffico medio dall'ultimo campione, o null se non calcolabile (primo campione o scarto).</summary>
    public InterfaceTrafficDto? Add(Guid deviceId, SnmpInterfaceCounters c, DateTimeOffset time)
    {
        var key = (deviceId, c.IfIndex);
        var current = new Baseline(c.UptimeTicks, c.InOctets, c.OutOctets, c.Is32Bit);

        // Prima lettura, o cambio fra contatori a 32 e 64 bit: i valori non sono confrontabili
        if (!baselines.TryGetValue(key, out var previous) || previous.Is32Bit != c.Is32Bit)
        {
            baselines[key] = current;
            return null;
        }

        if (c.UptimeTicks > previous.UptimeTicks && c.UptimeTicks - previous.UptimeTicks < MinTicks)
            return null;

        baselines[key] = current;

        if (c.UptimeTicks <= previous.UptimeTicks)
            return null;

        var seconds = (c.UptimeTicks - previous.UptimeTicks) / 100.0;
        if (Delta(previous.InOctets, c.InOctets, c.Is32Bit, seconds, c.SpeedBps) is not { } inBytes
            || Delta(previous.OutOctets, c.OutOctets, c.Is32Bit, seconds, c.SpeedBps) is not { } outBytes)
            return null;

        return new InterfaceTrafficDto(
            deviceId,
            c.IfIndex,
            time,
            (long)(inBytes * 8 / seconds),
            (long)(outBytes * 8 / seconds),
            c.SpeedBps,
            c.Name);
    }

    /// <summary>
    /// Byte trasferiti fra due letture; null se il contatore è stato azzerato. A 32 bit un calo è un giro del contatore,
    /// accettato se la velocità risultante non supera 1,5 volte quella dell'interfaccia (o 10 Gbit/s se ignota):
    /// altrimenti è un reset vero, o sono passati più giri e il valore non è attendibile.
    /// </summary>
    private static ulong? Delta(ulong previous, ulong current, bool is32Bit, double seconds, long? speedBps)
    {
        if (current >= previous)
            return current - previous;
        if (!is32Bit)
            return null;

        var wrapped = current + Wrap32 - previous;
        var maxBits = (speedBps is > 0 ? speedBps.Value * 1.5 : 10e9) * seconds;
        return wrapped * 8 <= maxBits ? wrapped : null;
    }

    /// <summary>Dimentica le basi del device (indirizzo cambiato: i contatori sono di un altro apparato).</summary>
    public void Reset(Guid deviceId)
    {
        foreach (var key in baselines.Keys.Where(k => k.DeviceId == deviceId))
            baselines.TryRemove(key, out _);
    }

    /// <summary>Tiene solo le interfacce ancora da misurare.</summary>
    public void RetainOnly(IEnumerable<AgentTargetDto> targets)
    {
        var keep = targets.SelectMany(t => t.IfIndexes.Select(i => (t.DeviceId, i))).ToHashSet();
        foreach (var key in baselines.Keys.Where(k => !keep.Contains(k)))
            baselines.TryRemove(key, out _);
    }
}
