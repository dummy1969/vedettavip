// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Api.Data.Entities;
using VedettaVip.Api.Services;
using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;

namespace VedettaVip.Tests;

public class RouterOsWatchTests
{
    private static readonly RouterOsSampleDto Sample = new(Guid.NewGuid(), DateTimeOffset.UtcNow, true,
        Interfaces: [new("ether1", "ether", true, false), new("ether2", "ether", false, false), new("ether3", "ether", false, true)],
        WireGuardPeers: [new("ok", "wg1", "filiale", null, null, 40, false), new("old", "wg1", null, null, null, 900, false),
                         new("never", "wg1", null, null, null, null, false), new("off", "wg1", null, null, null, 10, true)]);

    [Theory]
    [InlineData("ether1", true, "running")]
    [InlineData("ether2", false, "non running")]
    [InlineData("ether3", false, "disabilitata")]
    [InlineData("ether9", false, "non trovata")]
    public void Interfaces(string name, bool up, string detail)
    {
        var (isUp, text) = RouterOsWatchRules.Observe(RouterOsWatchKind.Interface, name, Sample)!.Value;
        Assert.Equal(up, isUp);
        Assert.StartsWith(detail, text);
    }

    [Theory]
    [InlineData("ok", true, "handshake 40 s fa")]
    [InlineData("old", false, "ultimo handshake 15 min fa")]
    [InlineData("never", false, "nessun handshake")]
    [InlineData("off", false, "peer disabilitato")]
    [InlineData("missing", false, "peer non trovato")]
    public void WireGuard_peers_by_last_handshake(string key, bool up, string detail)
    {
        var (isUp, text) = RouterOsWatchRules.Observe(RouterOsWatchKind.WireGuardPeer, key, Sample)!.Value;
        Assert.Equal((up, detail), (isUp, text.StartsWith(detail) ? detail : text));
    }

    [Fact]
    public void Lists_not_read_are_not_evaluated() =>
        Assert.Null(RouterOsWatchRules.Observe(RouterOsWatchKind.WireGuardPeer, "ok", Sample with { WireGuardPeers = null }));

    [Fact]
    public void Alert_opens_after_two_down_reads_and_closes_at_the_first_up()
    {
        var s = (State: RouterOsWatchState.Up, Reads: 0);
        var first = RouterOsWatchRules.Next(s.State, s.Reads, up: false);
        Assert.Equal((RouterOsWatchState.Up, 1, WatchTransition.None), first);   // un flap non basta
        var second = RouterOsWatchRules.Next(first.State, first.DownReads, up: false);
        Assert.Equal((RouterOsWatchState.Down, 2, WatchTransition.Raise), second);
        var still = RouterOsWatchRules.Next(second.State, second.DownReads, up: false);
        Assert.Equal(WatchTransition.None, still.Transition);                    // niente avvisi ripetuti
        Assert.Equal((RouterOsWatchState.Up, 0, WatchTransition.Clear), RouterOsWatchRules.Next(still.State, still.DownReads, up: true));
    }

    [Fact]
    public void Single_down_read_between_ups_is_forgotten()
    {
        var down = RouterOsWatchRules.Next(RouterOsWatchState.Up, 0, up: false);
        var up = RouterOsWatchRules.Next(down.State, down.DownReads, up: true);
        Assert.Equal((RouterOsWatchState.Up, 0, WatchTransition.None), up);
    }

    [Fact]
    public void First_read_up_is_not_a_recovery() =>
        Assert.Equal(WatchTransition.None, RouterOsWatchRules.Next(RouterOsWatchState.Unknown, 0, up: true).Transition);

    [Fact]
    public void Temperature_clears_five_degrees_below_threshold_cpu_at_80_percent()
    {
        Assert.Equal(70, ThresholdRules.ClearLevel(ThresholdKind.RouterOsTemperature, 75));
        Assert.Equal(72, ThresholdRules.ClearLevel(ThresholdKind.RouterOsCpu, 90));
        Assert.Equal(ThresholdTransition.None, ThresholdRules.Evaluate(true, 71, 75, ThresholdRules.ClearLevel(ThresholdKind.RouterOsTemperature, 75)));
        Assert.Equal(ThresholdTransition.Clear, ThresholdRules.Evaluate(true, 69, 75, ThresholdRules.ClearLevel(ThresholdKind.RouterOsTemperature, 75)));
        Assert.Equal(ThresholdTransition.Raise, ThresholdRules.Evaluate(false, 91, 90, ThresholdRules.ClearLevel(ThresholdKind.RouterOsCpu, 90)));
    }

    [Theory]
    [InlineData("if:ether1-wan", "[VedettaVip] INTERFACCIA GIÙ: core (10.0.0.1)", "[VedettaVip] INTERFACCIA RIPRISTINATA: core (10.0.0.1)")]
    [InlineData("wg:AbC=", "[VedettaVip] TUNNEL GIÙ: core (10.0.0.1)", "[VedettaVip] TUNNEL RIPRISTINATO: core (10.0.0.1)")]
    [InlineData("ros.cpu", "[VedettaVip] SOGLIA: core (10.0.0.1)", "[VedettaVip] RIENTRATO: core (10.0.0.1)")]
    public void Notification_subject_says_what_happened(string alertKey, string raised, string cleared)
    {
        AlertContext Context(AlertKind kind) => new(kind, "core", "10.0.0.1", null, [], DateTimeOffset.UtcNow, TimeZoneInfo.Utc, null, null, [], null,
            "core (10.0.0.1): interfaccia ether1-wan giù (non running)", AlertKey: alertKey);
        Assert.Equal(raised, AlertMessage.Build(Context(AlertKind.Threshold)).Subject);
        Assert.Equal(cleared, AlertMessage.Build(Context(AlertKind.ThresholdCleared)).Subject);
    }
}
