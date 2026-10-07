// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Lextm.SharpSnmpLib;
using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;
using VedettaVip.Worker.Services;
using VedettaVip.Worker.State;

namespace VedettaVip.Tests;

public class DeviceStateTrackerTests
{
    private static DeviceStateTracker NewTracker(int snmpDegradedAfter = 2) =>
        new(new StateThresholds(DownAfterFailures: 3, UpAfterSuccesses: 2, SnmpDegradedAfterFailures: snmpDegradedAfter));

    /// <summary>Porta il device a Up con 2 ping riusciti (senza SNMP).</summary>
    private static DeviceStateTracker UpTracker(int snmpDegradedAfter = 2)
    {
        var t = NewTracker(snmpDegradedAfter);
        t.RegisterIcmp(true);
        t.RegisterIcmp(true);
        Assert.Equal(NodeState.Up, t.State);
        return t;
    }

    [Fact]
    public void Starts_Unknown_and_stays_Unknown_until_a_threshold_is_reached()
    {
        var t = NewTracker();
        Assert.Equal(NodeState.Unknown, t.State);

        t.RegisterIcmp(false);
        t.RegisterIcmp(false);
        Assert.Equal(NodeState.Unknown, t.State);

        t.RegisterIcmp(true);
        Assert.Equal(NodeState.Unknown, t.State);
    }

    [Fact]
    public void Goes_Down_on_third_consecutive_icmp_failure()
    {
        var t = UpTracker();

        Assert.False(t.RegisterIcmp(false));
        Assert.False(t.RegisterIcmp(false));
        Assert.Equal(NodeState.Up, t.State);

        t.RegisterIcmp(false);
        Assert.Equal(NodeState.Down, t.State);
    }

    [Fact]
    public void Isolated_icmp_failure_does_not_change_state()
    {
        var t = UpTracker();

        t.RegisterIcmp(false);
        t.RegisterIcmp(false);
        t.RegisterIcmp(true); // azzera i fallimenti
        t.RegisterIcmp(false);
        t.RegisterIcmp(false);

        Assert.Equal(NodeState.Up, t.State);
    }

    [Fact]
    public void Returns_Up_from_Down_after_two_consecutive_successes()
    {
        var t = UpTracker();
        for (var i = 0; i < 3; i++) t.RegisterIcmp(false);
        Assert.Equal(NodeState.Down, t.State);

        Assert.False(t.RegisterIcmp(true));
        Assert.Equal(NodeState.Down, t.State);

        Assert.True(t.RegisterIcmp(true));
        Assert.Equal(NodeState.Up, t.State);
    }

    [Fact]
    public void Goes_Partial_after_two_consecutive_snmp_failures_and_back_Up_after_one_success()
    {
        var t = UpTracker();

        Assert.True(t.RegisterIcmp(true));
        t.RegisterSnmp(false);
        Assert.Equal(NodeState.Up, t.State); // un solo fallimento SNMP: ancora Up

        t.RegisterIcmp(true);
        t.RegisterSnmp(false);
        Assert.Equal(NodeState.Partial, t.State);

        t.RegisterIcmp(true);
        t.RegisterSnmp(true);
        Assert.Equal(NodeState.Up, t.State);
    }

    [Fact]
    public void Snmp_failure_count_resets_on_success()
    {
        var t = UpTracker();

        t.RegisterIcmp(true); t.RegisterSnmp(false);
        t.RegisterIcmp(true); t.RegisterSnmp(true);
        t.RegisterIcmp(true); t.RegisterSnmp(false);

        Assert.Equal(NodeState.Up, t.State);
    }

    [Fact]
    public void Snmp_threshold_is_configurable()
    {
        var t = UpTracker(snmpDegradedAfter: 1);

        t.RegisterIcmp(true);
        t.RegisterSnmp(false);

        Assert.Equal(NodeState.Partial, t.State);
    }

    [Fact]
    public void Partial_goes_Down_when_ping_fails_and_snmp_counter_restarts_after_recovery()
    {
        var t = UpTracker();
        t.RegisterIcmp(true); t.RegisterSnmp(false);
        t.RegisterIcmp(true); t.RegisterSnmp(false);
        Assert.Equal(NodeState.Partial, t.State);

        for (var i = 0; i < 3; i++) t.RegisterIcmp(false);
        Assert.Equal(NodeState.Down, t.State);

        t.RegisterIcmp(true);
        Assert.True(t.RegisterIcmp(true));
        Assert.Equal(NodeState.Up, t.State);

        t.RegisterSnmp(false);
        Assert.Equal(NodeState.Up, t.State); // il contatore SNMP è ripartito da zero
    }

    [Fact]
    public void Device_without_snmp_queries_never_becomes_Partial()
    {
        var t = UpTracker();
        for (var i = 0; i < 10; i++)
            t.RegisterIcmp(true); // il poller non chiama RegisterSnmp se SnmpVersion è None o V3

        Assert.Equal(NodeState.Up, t.State);
    }

    [Theory]
    [InlineData(SnmpVersion.None, null)]
    [InlineData(SnmpVersion.V3, null)]
    [InlineData(SnmpVersion.V1, VersionCode.V1)]
    [InlineData(SnmpVersion.V2c, VersionCode.V2)]
    public void Snmp_is_queried_only_for_v1_and_v2c(SnmpVersion version, VersionCode? expected) =>
        Assert.Equal(expected, PollingService.SnmpVersionCode(version));

    [Fact]
    public void Thresholds_changed_at_runtime_apply_to_the_ongoing_count()
    {
        var t = UpTracker();
        t.RegisterIcmp(false);
        t.RegisterIcmp(false);
        Assert.Equal(NodeState.Up, t.State); // 2 persi su 3

        // Dalla UI: Down dopo 5 ping persi. Il conteggio in corso (2) resta valido
        t.Thresholds = t.Thresholds with { DownAfterFailures = 5 };
        t.RegisterIcmp(false);
        t.RegisterIcmp(false);
        Assert.Equal(NodeState.Up, t.State);
        t.RegisterIcmp(false);
        Assert.Equal(NodeState.Down, t.State);

        // Ritorno Up dopo 1 solo ping riuscito
        t.Thresholds = t.Thresholds with { UpAfterSuccesses = 1 };
        t.RegisterIcmp(true);
        Assert.Equal(NodeState.Up, t.State);
    }

    [Fact]
    public void Device_overrides_replace_only_the_values_they_specify()
    {
        var general = new DetectionThresholdsDto(3, 2, 2);
        Assert.Equal(new DetectionThresholdsDto(6, 2, 2), general.WithOverrides(6, null, null));
        Assert.Equal(general, general.WithOverrides(null, null, null));
        Assert.Equal(new DetectionThresholdsDto(1, 4, 5), general.WithOverrides(1, 4, 5));
    }
}
