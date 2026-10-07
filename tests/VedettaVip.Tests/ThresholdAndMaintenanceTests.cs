// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Api.Services;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Tests;

public class ThresholdRulesTests
{
    [Theory]
    [InlineData(false, 150, 100, ThresholdTransition.Raise)]   // sopra soglia: si apre
    [InlineData(false, 100, 100, ThresholdTransition.None)]    // uguale: no
    [InlineData(true, 90, 100, ThresholdTransition.None)]      // tra 80% e 100%: resta aperto (isteresi)
    [InlineData(true, 79, 100, ThresholdTransition.Clear)]     // sotto 80%: si chiude
    [InlineData(false, 90, 100, ThresholdTransition.None)]
    public void Hysteresis(bool open, double value, double threshold, ThresholdTransition expected) =>
        Assert.Equal(expected, ThresholdRules.Evaluate(open, value, threshold));

    [Fact]
    public void Disabled_threshold_closes_open_alerts_and_opens_nothing()
    {
        Assert.Equal(ThresholdTransition.Clear, ThresholdRules.Evaluate(true, 500, null));
        Assert.Equal(ThresholdTransition.None, ThresholdRules.Evaluate(false, 500, null));
    }

    [Theory]
    [InlineData(null, 100.0, 100.0)]  // nessun valore specifico: generale
    [InlineData(0.0, 100.0, null)]    // 0: disattivata sul device
    [InlineData(300.0, 100.0, 300.0)] // specifica
    [InlineData(null, null, null)]    // generale disattivata
    public void Effective_threshold(double? specific, double? general, double? expected) =>
        Assert.Equal(expected, ThresholdRules.Effective(specific, general));
}

public class MaintenanceScheduleTests
{
    private static readonly TimeZoneInfo Rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");

    private static MaintenanceSchedule.Window Weekly(int days, int hour, int durationMinutes) =>
        new(MaintenanceRecurrence.Weekly, null, null, days, new TimeOnly(hour, 0), durationMinutes, true);

    private static DateTimeOffset RomeTime(int y, int m, int d, int h, int min) =>
        new(new DateTime(y, m, d, h, min, 0), Rome.GetUtcOffset(new DateTime(y, m, d, h, min, 0)));

    [Fact]
    public void Once_is_active_between_start_and_end()
    {
        var w = new MaintenanceSchedule.Window(MaintenanceRecurrence.Once, RomeTime(2026, 10, 12, 22, 0), RomeTime(2026, 10, 13, 2, 0), 0, null, 0, true);
        Assert.True(MaintenanceSchedule.IsActive(w, RomeTime(2026, 10, 12, 23, 30), Rome));
        Assert.False(MaintenanceSchedule.IsActive(w, RomeTime(2026, 10, 13, 2, 0), Rome)); // fine esclusa
        Assert.False(MaintenanceSchedule.IsActive(w with { Enabled = false }, RomeTime(2026, 10, 12, 23, 30), Rome));
    }

    [Fact]
    public void Weekly_window_crossing_midnight_covers_the_next_day()
    {
        // Domenica (bit 0) dalle 23:00 per 4 ore: copre lunedì fino alle 03:00. 11/10/2026 è domenica.
        var w = Weekly(1 << 0, 23, 240);
        Assert.True(MaintenanceSchedule.IsActive(w, RomeTime(2026, 10, 11, 23, 30), Rome));
        Assert.True(MaintenanceSchedule.IsActive(w, RomeTime(2026, 10, 12, 2, 59), Rome));
        Assert.False(MaintenanceSchedule.IsActive(w, RomeTime(2026, 10, 12, 3, 0), Rome));
        Assert.False(MaintenanceSchedule.IsActive(w, RomeTime(2026, 10, 10, 23, 30), Rome)); // sabato no
    }

    [Fact]
    public void Weekly_window_follows_local_time_across_daylight_saving()
    {
        // Ogni giorno alle 02:00 per 1 ora. Il 25/10/2026 in Italia finisce l'ora legale: resta alle 02:00 locali.
        var w = Weekly(0b111_1111, 2, 60);
        Assert.True(MaintenanceSchedule.IsActive(w, RomeTime(2026, 10, 24, 2, 30), Rome));
        Assert.True(MaintenanceSchedule.IsActive(w, RomeTime(2026, 10, 26, 2, 30), Rome));
        Assert.False(MaintenanceSchedule.IsActive(w, RomeTime(2026, 10, 26, 1, 30), Rome));
    }

    [Fact]
    public void Next_start_is_the_following_occurrence()
    {
        var w = Weekly(1 << 0, 23, 240); // domenica 23:00
        var next = MaintenanceSchedule.NextStart(w, RomeTime(2026, 10, 7, 12, 0), Rome); // mercoledì
        Assert.Equal(RomeTime(2026, 10, 11, 23, 0), next);
    }
}
