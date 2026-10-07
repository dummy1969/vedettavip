// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.ComponentModel.DataAnnotations;

namespace VedettaVip.Worker.Options;

/// <summary>Sezione "Agent": collegamento all'API centrale.</summary>
public sealed class AgentOptions
{
    public const string Section = "Agent";

    [Required, Url] public string ApiBaseUrl { get; set; } = "";

    /// <summary>Sviluppo: user-secrets; produzione: variabile Agent__ApiKey.</summary>
    [Required(ErrorMessage = "Agent:ApiKey mancante (user-secrets in sviluppo, Agent__ApiKey in produzione).")]
    [MinLength(32, ErrorMessage = "Agent:ApiKey deve avere almeno 32 caratteri.")]
    public string ApiKey { get; set; } = "";

    [Required, StringLength(64, MinimumLength = 1)]
    public string AgentId { get; set; } = Environment.MachineName;

    [Range(10, 3600)] public int TargetsRefreshSeconds { get; set; } = 120;

    /// <summary>Deve coincidere con Agent:SnapshotIntervalSeconds dell'API.</summary>
    [Range(30, 3600)] public int SnapshotIntervalSeconds { get; set; } = 300;

    /// <summary>Cambi di stato in attesa di invio quando l'API non risponde; oltre si scartano i più vecchi.</summary>
    [Range(100, 1_000_000)] public int BufferCapacity { get; set; } = 10_000;

    [Range(1, 1000)] public int MaxBatchSize { get; set; } = 500;
    [Range(1, 60)] public int RetryInitialSeconds { get; set; } = 1;
    [Range(1, 3600)] public int RetryMaxSeconds { get; set; } = 60;
    [Range(1, 120)] public int HttpTimeoutSeconds { get; set; } = 10;
}
