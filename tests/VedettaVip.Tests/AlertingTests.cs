// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Api.Data.Entities;
using VedettaVip.Api.Services;
using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;

namespace VedettaVip.Tests;

public class AlertingTests
{
    [Theory]
    [InlineData(NodeState.Up, NodeState.Down, AlertKind.Down)]
    [InlineData(NodeState.Unknown, NodeState.Down, AlertKind.Down)]
    [InlineData(NodeState.Up, NodeState.Partial, AlertKind.Partial)]
    [InlineData(NodeState.Down, NodeState.Up, AlertKind.Recovery)]
    [InlineData(NodeState.Partial, NodeState.Up, AlertKind.Recovery)]
    public void State_changes_that_generate_an_alert(NodeState from, NodeState to, AlertKind expected) =>
        Assert.Equal(expected, AlertRules.KindOf(EventTypes.StateChange, from, to));

    [Fact]
    public void First_detection_as_up_is_not_an_alert() =>
        Assert.Null(AlertRules.KindOf(EventTypes.StateChange, NodeState.Unknown, NodeState.Up));

    [Theory]
    // Down: tutti
    [InlineData(AlertFilter.DownOnly, false, AlertKind.Down, NodeState.Up, true)]
    // Partial: solo con "Down e Partial"
    [InlineData(AlertFilter.DownOnly, true, AlertKind.Partial, NodeState.Up, false)]
    [InlineData(AlertFilter.DownAndPartial, true, AlertKind.Partial, NodeState.Up, true)]
    // Ripristino: solo se richiesto e se il problema era tra quelli notificati
    [InlineData(AlertFilter.DownOnly, true, AlertKind.Recovery, NodeState.Down, true)]
    [InlineData(AlertFilter.DownOnly, false, AlertKind.Recovery, NodeState.Down, false)]
    [InlineData(AlertFilter.DownOnly, true, AlertKind.Recovery, NodeState.Partial, false)]
    [InlineData(AlertFilter.DownAndPartial, true, AlertKind.Recovery, NodeState.Partial, true)]
    public void Subscription_filters(AlertFilter filter, bool recovery, AlertKind kind, NodeState from, bool expected) =>
        Assert.Equal(expected, AlertRules.Qualifies(filter, recovery, kind, from));

    [Fact]
    public void Retry_delay_grows_and_attempts_are_bounded()
    {
        var delays = Enumerable.Range(1, AlertRules.MaxAttempts).Select(AlertRules.RetryDelay).ToList();
        Assert.Equal(delays.Order(), delays);
        Assert.Equal(TimeSpan.FromMinutes(1), delays[0]);
    }

    [Fact]
    public void Down_message_lists_customer_maps_and_unreachable_dependents()
    {
        var rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");
        var (subject, body) = AlertMessage.Build(new AlertContext(
            AlertKind.Down, "router-01", "10.1.0.1", "Cliente Demo Srl", ["Sede Demo"],
            new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero), rome, NodeState.Up, null,
            ["AP-1", "SW-1"], "https://vedettavip.example/map/1"));

        Assert.Equal("[VedettaVip] DOWN: router-01 (10.1.0.1)", subject);
        Assert.Contains("12:00:00 del 05/10/2026", body); // ora locale
        Assert.Contains("Cliente: Cliente Demo Srl", body);
        Assert.Contains("Mappe: Sede Demo", body);
        Assert.Contains("Dispositivi dipendenti non raggiungibili (2): AP-1, SW-1", body);
        Assert.EndsWith("https://vedettavip.example/map/1", body);
    }

    [Fact]
    public void Recovery_message_shows_problem_duration()
    {
        var (subject, body) = AlertMessage.Build(new AlertContext(
            AlertKind.Recovery, "router-02", "192.0.2.254", null, [], DateTimeOffset.UtcNow, TimeZoneInfo.Utc,
            NodeState.Down, TimeSpan.FromMinutes(75), [], null));

        Assert.Equal("[VedettaVip] RIPRISTINO: router-02 (192.0.2.254) dopo 1 h 15 min", subject);
        Assert.Contains("era Down da 1 h 15 min", body);
    }

    [Theory]
    [InlineData(42, "42 s")]
    [InlineData(12 * 60, "12 min")]
    [InlineData(3 * 3600, "3 h")]
    [InlineData(2 * 86400 + 4 * 3600, "2 g 4 h")]
    public void Durations_are_readable(int seconds, string expected) =>
        Assert.Equal(expected, AlertMessage.Duration(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(AlertFilter.DownOnly, AlertKind.Threshold, false)]
    [InlineData(AlertFilter.DownAndPartial, AlertKind.Threshold, false)]
    [InlineData(AlertFilter.AllProblems, AlertKind.Threshold, true)]
    [InlineData(AlertFilter.AllProblems, AlertKind.Partial, true)]
    [InlineData(AlertFilter.AllProblems, AlertKind.ThresholdCleared, true)]
    [InlineData(AlertFilter.DownAndPartial, AlertKind.ThresholdCleared, false)]
    public void Thresholds_only_for_all_problems_subscriptions(AlertFilter filter, AlertKind kind, bool expected) =>
        Assert.Equal(expected, AlertRules.Qualifies(filter, notifyRecovery: true, kind, NodeState.Up));

    [Theory]
    [InlineData(AlertKind.Down, false, true)]
    [InlineData(AlertKind.AgentOffline, false, true)]
    [InlineData(AlertKind.Partial, false, false)]
    [InlineData(AlertKind.Threshold, false, false)]
    [InlineData(AlertKind.Partial, true, true)]
    [InlineData(AlertKind.Threshold, true, true)]
    [InlineData(AlertKind.Recovery, true, false)]
    public void Reminders_for_down_and_optionally_warnings(AlertKind kind, bool includeWarnings, bool expected) =>
        Assert.Equal(expected, AlertRules.RemindAbout(kind, includeWarnings));

    [Fact]
    public void Threshold_message_uses_the_event_detail()
    {
        var (subject, body) = AlertMessage.Build(new AlertContext(
            AlertKind.Threshold, "router-01", "10.1.0.1", null, [], DateTimeOffset.UtcNow, TimeZoneInfo.Utc, null, null, [], null,
            "router-01 (10.1.0.1): latenza media 152 ms negli ultimi 5 min, soglia 100 ms"));

        Assert.Equal("[VedettaVip] SOGLIA: router-01 (10.1.0.1)", subject);
        Assert.StartsWith("Latenza media 152 ms negli ultimi 5 min, soglia 100 ms.", body);
    }

    [Fact]
    public void Reminder_message_says_how_long_it_has_been_open()
    {
        var (subject, body) = AlertMessage.Build(new AlertContext(
            AlertKind.Down, "router-02", "192.0.2.254", null, [], DateTimeOffset.UtcNow, TimeZoneInfo.Utc, NodeState.Up,
            TimeSpan.FromMinutes(130), [], null, IsReminder: true));

        Assert.Equal("[VedettaVip] PROMEMORIA DOWN: router-02 (192.0.2.254)", subject);
        Assert.StartsWith("Problema ancora aperto da 2 h 10 min e non preso in carico.", body);
    }
}
