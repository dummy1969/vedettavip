// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

namespace VedettaVip.Api.Data.Entities;

/// <summary>
/// Impostazioni generali del monitoraggio, una sola riga (Id = 1). Le soglie di rilevazione valgono per tutti i
/// device che non hanno un valore specifico (Device.DownAfterFailures ecc.).
/// </summary>
public sealed class MonitoringSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public int DownAfterFailures { get; set; }
    public int UpAfterSuccesses { get; set; }
    public int SnmpDegradedAfterFailures { get; set; }

    // Soglie generali sulle metriche (media sulla finestra); null = disattivata
    public int? RttThresholdMs { get; set; }
    public double? LossThresholdPct { get; set; }
    public int? LinkUtilizationThresholdPct { get; set; }
    public int ThresholdWindowMinutes { get; set; }
    // Soglie RouterOS generali (media sulla finestra); null = disattivata
    public int? RouterOsCpuThresholdPct { get; set; }
    public int? RouterOsTemperatureThresholdC { get; set; }

    /// <summary>Ore di eventi recenti mostrate nella dashboard (Home).</summary>
    public int DashboardEventHours { get; set; }

    /// <summary>Percorso di WinBox 4 sui PC Windows degli operatori, scritto nell'installer del gestore winbox://; null = ricerca.</summary>
    public string? WinBoxWindowsPath { get; set; }
    /// <summary>Percorso di WinBox 4 sui PC Linux degli operatori, scritto nell'installer del gestore winbox://; null = ricerca.</summary>
    public string? WinBoxLinuxPath { get; set; }

    /// <summary>Profilo SNMP dei device senza profilo proprio né del cliente; null = community del Worker.</summary>
    public Guid? DefaultSnmpCredentialId { get; set; }

    /// <summary>Profilo RouterOS dei device con API abilitata senza profilo proprio né del cliente; null = nessuno.</summary>
    public Guid? DefaultRouterOsCredentialId { get; set; }
}
