// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.ComponentModel.DataAnnotations;

namespace VedettaVip.Worker.Options;

/// <summary>Sezione "Polling": tempi e soglie del poller.</summary>
public sealed class PollingOptions
{
    public const string Section = "Polling";

    [Range(5, 3600)] public int IntervalSeconds { get; set; } = 30;
    [Range(100, 30_000)] public int IcmpTimeoutMs { get; set; } = 1500;
    [Range(100, 30_000)] public int SnmpTimeoutMs { get; set; } = 3000;
    [Range(1, 1024)] public int MaxDegreeOfParallelism { get; set; } = 32;

    // Soglie di rilevazione: si configurano dalla UI (Impostazioni e singolo device) e arrivano con i target.
    // Questi valori servono solo se l'API non le invia.

    /// <summary>Poll ICMP falliti consecutivi per passare a Down (ripiego, vedi sopra).</summary>
    [Range(1, 100)] public int DownAfterFailures { get; set; } = 3;

    /// <summary>Poll ICMP riusciti consecutivi per tornare raggiungibile da Down/Unknown.</summary>
    [Range(1, 100)] public int UpAfterSuccesses { get; set; } = 2;

    /// <summary>Fallimenti SNMP consecutivi (con ping ok) per passare a Partial; il ritorno a Up avviene al primo successo.</summary>
    [Range(1, 100)] public int SnmpDegradedAfterFailures { get; set; } = 2;

    /// <summary>Ogni quanto rileggere l'elenco delle interfacce dei device SNMP v2c (più spesso su richiesta dalla UI).</summary>
    [Range(1, 1440)] public int InterfaceInventoryMinutes { get; set; } = 15;

    /// <summary>Ogni quanto rileggere i vicini (Discovery: /ip/neighbor, LLDP, CDP) di ogni device.</summary>
    [Range(5, 1440)] public int NeighborDiscoveryMinutes { get; set; } = 30;

    /// <summary>Ogni quanto leggere risorse e salute dei router con API RouterOS abilitata.</summary>
    [Range(15, 3600)] public int RouterOsIntervalSeconds { get; set; } = 60;

    /// <summary>Tempo massimo per connessione, login e lettura di un router.</summary>
    [Range(1000, 60_000)] public int RouterOsTimeoutMs { get; set; } = 8000;

    /// <summary>
    /// Community SNMP v1/v2c di ripiego: usata solo per i device senza profilo SNMP effettivo
    /// (né del device, né del cliente, né predefinito: pagina Impostazioni → Profili SNMP).
    /// </summary>
    [Required] public string SnmpCommunity { get; set; } = "public";
}
