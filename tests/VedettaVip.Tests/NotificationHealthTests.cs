// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Api.Data.Entities;
using VedettaVip.Api.Services;
using VedettaVip.Shared.Contracts;
using static VedettaVip.Api.Services.NotificationHealthRules;

namespace VedettaVip.Tests;

public class NotificationHealthTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 19, 13, 54, 0, TimeSpan.Zero);

    [Fact]
    public void Channel_without_failures_is_ok()
    {
        var h = Evaluate(NotificationChannel.Email, T0, []);
        Assert.False(h.Failing);
        Assert.Null(h.FailingSince);
        Assert.Equal(T0, h.LastSentAt);
    }

    [Fact]
    public void Fewer_than_three_attempts_are_not_yet_a_failure()
    {
        var h = Evaluate(NotificationChannel.Email, T0, [new FailedDelivery(1, T0.AddMinutes(1), 2, "timeout")]);
        Assert.False(h.Failing);
        Assert.Equal(2, h.FailedAttempts);
        Assert.Null(h.LastError);
    }

    [Fact]
    public void Attempts_after_the_last_success_add_up_across_deliveries()
    {
        var h = Evaluate(NotificationChannel.Email, T0,
        [
            new FailedDelivery(1, T0.AddMinutes(1), 2, "Resource temporarily unavailable"),
            new FailedDelivery(2, T0.AddMinutes(5), 1, "Name or service not known")
        ]);
        Assert.True(h.Failing);
        Assert.Equal(T0.AddMinutes(1), h.FailingSince);
        Assert.Equal(3, h.FailedAttempts);
        Assert.Equal(2, h.FailedDeliveries);
        Assert.Equal("Name or service not known", h.LastError); // l'invio più recente
    }

    [Fact]
    public void Failures_created_before_the_last_success_concern_the_recipient_not_the_channel()
    {
        // Un indirizzo sbagliato fallisce 8 volte, ma un invio successivo a un altro destinatario è riuscito
        var h = Evaluate(NotificationChannel.Email, T0, [new FailedDelivery(1, T0.AddMinutes(-10), 8, "mailbox unavailable")]);
        Assert.False(h.Failing);
        Assert.Equal(0, h.FailedAttempts);
    }

    [Fact]
    public void Channel_that_never_sent_anything_fails_on_its_attempts()
    {
        var h = Evaluate(NotificationChannel.Telegram, null, [new FailedDelivery(1, T0, 3, "401 Unauthorized")]);
        Assert.True(h.Failing);
        Assert.Null(h.LastSentAt);
    }

    [Fact]
    public void Deliveries_never_attempted_do_not_count() =>
        Assert.False(Evaluate(NotificationChannel.Email, null, [new FailedDelivery(1, T0, 0, null)]).Failing);

    [Theory]
    [InlineData(NotificationChannel.Email, "notify:Email")]
    [InlineData(NotificationChannel.Telegram, "notify:Telegram")]
    public void Alert_key_round_trip(NotificationChannel channel, string key)
    {
        Assert.Equal(key, AlertKey(channel));
        Assert.Equal(channel, ChannelOf(key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("latency")]
    [InlineData("if:ether1")]
    [InlineData("notify:Sms")]
    public void Other_alert_keys_have_no_channel(string? key) => Assert.Null(ChannelOf(key));

    [Fact]
    public void Channel_events_are_notified_to_everybody_and_failure_skips_its_own_channel()
    {
        Assert.Equal(AlertKind.ChannelFailing, AlertRules.KindOf(EventTypes.NotificationsFailing, null, null));
        Assert.Equal(AlertKind.ChannelRestored, AlertRules.KindOf(EventTypes.NotificationsRestored, null, null));

        // Anche chi riceve solo i Down e non i ripristini: il ripristino dice quanti avvisi sono andati persi
        Assert.True(AlertRules.Qualifies(AlertFilter.DownOnly, false, AlertKind.ChannelFailing, null));
        Assert.True(AlertRules.Qualifies(AlertFilter.DownOnly, false, AlertKind.ChannelRestored, null));

        Assert.Equal(NotificationChannel.Email, AlertRules.ExcludedChannel(AlertKind.ChannelFailing, "notify:Email"));
        Assert.Null(AlertRules.ExcludedChannel(AlertKind.ChannelRestored, "notify:Email"));
        Assert.Null(AlertRules.ExcludedChannel(AlertKind.Down, null));
    }

    [Fact]
    public void Channel_failure_message()
    {
        var (subject, body) = AlertMessage.Build(new AlertContext(AlertKind.ChannelFailing, "email", null, null, [], T0,
            TimeZoneInfo.Utc, null, null, [], "https://vedettavip.example.com/events",
            "Le notifiche email non vengono inviate dal 19/10 15:54: 6 tentativi falliti su 2 invii.", AlertKey: "notify:Email"));

        Assert.Equal("[VedettaVip] NOTIFICHE EMAIL NON INVIATE", subject);
        Assert.StartsWith("Le notifiche email non vengono inviate dal 19/10 15:54", body);
        Assert.EndsWith("https://vedettavip.example.com/events", body);
    }

    [Fact]
    public void Banner_text()
    {
        var h = new NotificationChannelHealthDto(NotificationChannel.Email, true, null, 4, 1, "Resource temporarily unavailable", null);
        Assert.Equal("Le notifiche email non vengono inviate: 4 tentativi falliti su 1 invio. " +
                     "Ultimo errore: Resource temporarily unavailable. Gli avvisi su questo canale non arrivano.", h.Warning());
        Assert.Null((h with { Failing = false }).Warning());
    }
}
