// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VedettaVip.Api.Data.Entities;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Data.Configurations;

public sealed class MonitoringSettingsConfiguration : IEntityTypeConfiguration<MonitoringSettings>
{
    public void Configure(EntityTypeBuilder<MonitoringSettings> b)
    {
        b.ToTable("MonitoringSettings", t =>
        {
            t.HasCheckConstraint("CK_MonitoringSettings_Singleton", $"\"Id\" = {MonitoringSettings.SingletonId}");
            t.HasCheckConstraint("CK_MonitoringSettings_Window", "\"ThresholdWindowMinutes\" BETWEEN 1 AND 60");
            t.HasCheckConstraint("CK_MonitoringSettings_DashboardHours",
                $"\"DashboardEventHours\" BETWEEN 1 AND {DashboardSettingsDto.MaxEventHours}");
            t.HasCheckConstraint("CK_MonitoringSettings_Thresholds",
                "\"DownAfterFailures\" BETWEEN 1 AND 100 AND \"UpAfterSuccesses\" BETWEEN 1 AND 100 " +
                "AND \"SnmpDegradedAfterFailures\" BETWEEN 1 AND 100");
        });
        b.Property(s => s.Id).ValueGeneratedNever();

        // La riga esiste sempre: i valori iniziali sono quelli usati prima che fossero configurabili
        var defaults = DetectionThresholdsDto.Default;
        b.HasData(new MonitoringSettings
        {
            Id = MonitoringSettings.SingletonId,
            DownAfterFailures = defaults.DownAfterFailures,
            UpAfterSuccesses = defaults.UpAfterSuccesses,
            SnmpDegradedAfterFailures = defaults.SnmpDegradedAfterFailures,
            RttThresholdMs = MetricThresholdsDto.Default.RttMs,
            LossThresholdPct = MetricThresholdsDto.Default.LossPct,
            LinkUtilizationThresholdPct = MetricThresholdsDto.Default.LinkUtilizationPct,
            ThresholdWindowMinutes = MetricThresholdsDto.Default.WindowMinutes,
            DashboardEventHours = DashboardSettingsDto.Default.RecentEventHours,
            RouterOsCpuThresholdPct = MetricThresholdsDto.Default.CpuPct,
            RouterOsTemperatureThresholdC = MetricThresholdsDto.Default.TemperatureC
        });
    }
}
