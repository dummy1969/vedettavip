// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Api.Services;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Tests;

public class MetricResolutionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(1, MetricSource.Raw, 30)]              // 1 h: grezzi a 30 s (120 punti)
    [InlineData(6, MetricSource.Raw, 60)]              // 6 h: 360 punti a 1 min
    [InlineData(24, MetricSource.FiveMinutes, 300)]      // 24 h: 288 punti a 5 min, dall'aggregato
    [InlineData(24 * 7, MetricSource.FiveMinutes, 30 * 60)]  // 7 giorni: 30 min dall'aggregato a 5 min
    [InlineData(24 * 30, MetricSource.OneHour, 2 * 3600)]    // 30 giorni: 2 h dall'aggregato orario
    public void Chooses_source_and_bucket_for_about_500_points(int hours, MetricSource source, int bucketSeconds)
    {
        var (s, bucket) = MetricResolution.Choose(Now - TimeSpan.FromHours(hours), Now, Now);
        Assert.Equal(source, s);
        Assert.Equal(TimeSpan.FromSeconds(bucketSeconds), bucket);
        Assert.InRange(TimeSpan.FromHours(hours) / bucket, 1, MetricResolution.TargetPoints);
    }

    [Fact]
    public void Short_range_older_than_raw_retention_uses_five_minute_aggregate()
    {
        var from = Now - TimeSpan.FromDays(10);
        var (s, bucket) = MetricResolution.Choose(from, from + TimeSpan.FromHours(1), Now);
        Assert.Equal(MetricSource.FiveMinutes, s);
        Assert.Equal(TimeSpan.FromMinutes(5), bucket);
    }

    [Fact]
    public void Range_older_than_five_minute_retention_uses_hourly_aggregate()
    {
        var from = Now - TimeSpan.FromDays(120);
        var (s, bucket) = MetricResolution.Choose(from, from + TimeSpan.FromHours(6), Now);
        Assert.Equal(MetricSource.OneHour, s);
        Assert.Equal(TimeSpan.FromHours(1), bucket);
    }
}
