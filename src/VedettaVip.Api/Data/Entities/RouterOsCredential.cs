// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

namespace VedettaVip.Api.Data.Entities;

/// <summary>
/// Profilo di accesso all'API RouterOS (servizio "api" in chiaro o "api-ssl" con TLS, porta configurabile). Password
/// cifrata con Data Protection, mai restituita alla UI. Profilo effettivo di un device con l'API abilitata: il suo,
/// altrimenti quello del cliente, altrimenti il predefinito (MonitoringSettings.DefaultRouterOsCredentialId).
/// </summary>
public sealed class RouterOsCredential
{
    public const int DefaultApiPort = 8728, DefaultApiSslPort = 8729;

    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string Username { get; set; }
    public required string PasswordProtected { get; set; }
    public bool UseTls { get; set; }
    public int Port { get; set; }
    /// <summary>Con TLS: verifica del certificato del router (spesso autofirmato, quindi disattivata di solito).</summary>
    public bool VerifyCertificate { get; set; }
}
