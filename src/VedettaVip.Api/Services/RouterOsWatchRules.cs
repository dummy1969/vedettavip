// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Api.Data.Entities;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Services;

public enum WatchTransition { None, Raise, Clear }

/// <summary>
/// Regole della sorveglianza di interfacce e peer WireGuard (pure, coperte da test). Un elemento è giù se:
/// interfaccia non running, disabilitata o sparita dal router; peer WireGuard disabilitato, senza handshake o con
/// l'ultimo handshake più vecchio di <see cref="WireGuardHandshakeMaxSeconds"/> (su RouterOS l'interfaccia WireGuard
/// resta running anche con il tunnel fermo). L'avviso si apre dopo <see cref="DownAfterReads"/> letture giù di fila.
/// </summary>
public static class RouterOsWatchRules
{
    /// <summary>
    /// Con persistent-keepalive il tunnel rinnova l'handshake ogni 2 minuti circa: oltre i 3 minuti il peer è fermo.
    /// Senza keepalive l'handshake avviene solo con traffico: un tunnel inattivo risulterebbe giù.
    /// </summary>
    public const int WireGuardHandshakeMaxSeconds = 180;

    public const int DownAfterReads = 2;

    /// <summary>
    /// Stato osservato nella lettura; null = non valutabile (lista non letta: RouterOS v6 per WireGuard, lettura fallita).
    /// </summary>
    public static (bool Up, string Detail)? Observe(RouterOsWatchKind kind, string key, RouterOsSampleDto sample) => kind switch
    {
        RouterOsWatchKind.Interface when sample.Interfaces is { } list =>
            list.FirstOrDefault(i => i.Name == key) is not { } i ? (false, "non trovata sul router (rinominata o eliminata?)")
            : i.Disabled ? (false, "disabilitata")
            : i.Running ? (true, "running")
            : (false, "non running: link giù o tunnel non connesso"),

        RouterOsWatchKind.WireGuardPeer when sample.WireGuardPeers is { } peers =>
            peers.FirstOrDefault(p => p.PublicKey == key) is not { } p ? (false, "peer non trovato sul router")
            : p.Disabled ? (false, "peer disabilitato")
            : p.LastHandshakeSeconds is not { } s ? (false, "nessun handshake")
            : s > WireGuardHandshakeMaxSeconds ? (false, $"ultimo handshake {Ago(s)} fa")
            : (true, $"handshake {Ago(s)} fa"),

        _ => null
    };

    /// <summary>Nuovo stato, letture giù consecutive e transizione dell'avviso.</summary>
    public static (RouterOsWatchState State, int DownReads, WatchTransition Transition) Next(RouterOsWatchState state, int downReads, bool up)
    {
        if (up)
            return (RouterOsWatchState.Up, 0, state == RouterOsWatchState.Down ? WatchTransition.Clear : WatchTransition.None);
        if (state == RouterOsWatchState.Down)
            return (state, downReads + 1, WatchTransition.None);
        var reads = downReads + 1;
        return reads >= DownAfterReads
            ? (RouterOsWatchState.Down, reads, WatchTransition.Raise)
            : (state, reads, WatchTransition.None);
    }

    /// <summary>Chiave dell'avviso (Event.AlertKey): "if:ether1", "wg:&lt;chiave pubblica&gt;".</summary>
    public static string AlertKey(RouterOsWatchKind kind, string key) => kind == RouterOsWatchKind.Interface ? $"if:{key}" : $"wg:{key}";

    private static string Ago(long seconds) => seconds switch
    {
        < 120 => $"{seconds} s",
        < 7200 => $"{seconds / 60} min",
        < 172_800 => $"{seconds / 3600} h",
        _ => $"{seconds / 86_400} g"
    };
}
