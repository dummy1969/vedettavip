// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.ComponentModel.DataAnnotations;

namespace VedettaVip.Shared.Contracts;

/// <summary>Profilo SNMP per la UI: la community non viene mai restituita.</summary>
public sealed record SnmpCredentialDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsDefault,
    int DeviceCount,
    int CustomerCount);

/// <param name="Community">Obbligatoria alla creazione; nel PUT null o vuota = invariata.</param>
public sealed record SnmpCredentialUpsertDto(
    [property: Required(ErrorMessage = "Il nome è obbligatorio."), StringLength(128, ErrorMessage = "Il nome supera i 128 caratteri.")] string Name,
    [property: StringLength(512)] string? Description = null,
    [property: StringLength(255, ErrorMessage = "La community supera i 255 caratteri.")] string? Community = null);

/// <summary>Profilo predefinito (device senza profilo proprio né del cliente); null = community del Worker.</summary>
public sealed record SnmpDefaultDto(Guid? CredentialId);
