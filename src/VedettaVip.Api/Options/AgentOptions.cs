// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.ComponentModel.DataAnnotations;

namespace VedettaVip.Api.Options;

/// <summary>Sezione "Agent": autenticazione e tempi degli agenti di polling.</summary>
public sealed class AgentOptions
{
    public const string Section = "Agent";

    /// <summary>Chiave condivisa con gli agenti. Sviluppo: user-secrets; produzione: variabile Agent__ApiKey.</summary>
    [Required(ErrorMessage = "Agent:ApiKey mancante (user-secrets in sviluppo, Agent__ApiKey in produzione).")]
    [MinLength(32, ErrorMessage = "Agent:ApiKey deve avere almeno 32 caratteri.")]
    public string ApiKey { get; set; } = "";

    /// <summary>Intervallo di snapshot degli agenti: deve coincidere con Agent:SnapshotIntervalSeconds del Worker.</summary>
    [Range(30, 3600)]
    public int SnapshotIntervalSeconds { get; set; } = 300;

    /// <summary>
    /// Età massima di un campione di traffico per essere mostrato (default 90 s = 3 cicli di polling da 30 s):
    /// oltre, il link è "n/d" (agente fermo, device non raggiungibile o SNMP ko).
    /// </summary>
    [Range(10, 3600)]
    public int TrafficStaleSeconds { get; set; } = 90;

    /// <summary>Età massima di una lettura RouterOS mostrata come attuale (il Worker legge ogni 60 s).</summary>
    [Range(30, 3600)] public int RouterOsStaleSeconds { get; set; } = 180;

    /// <summary>Oltre 2 intervalli di snapshot senza report lo stato è vecchio (Unknown) e l'agente è offline.</summary>
    public TimeSpan StaleAfter => TimeSpan.FromSeconds(SnapshotIntervalSeconds * 2);
}
