// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.ComponentModel.DataAnnotations;
using VedettaVip.Shared.Models;

namespace VedettaVip.Shared.Contracts;

/// <summary>Profilo RouterOS per la UI: la password non viene mai restituita.</summary>
public sealed record RouterOsCredentialDto(
    Guid Id,
    string Name,
    string? Description,
    string Username,
    bool UseTls,
    int Port,
    bool VerifyCertificate,
    bool IsDefault,
    int DeviceCount,
    int CustomerCount);

/// <param name="Password">Obbligatoria alla creazione; nel PUT null o vuota = invariata.</param>
public sealed record RouterOsCredentialUpsertDto(
    [property: Required(ErrorMessage = "Il nome è obbligatorio."), StringLength(128, ErrorMessage = "Il nome supera i 128 caratteri.")] string Name,
    [property: Required(ErrorMessage = "L'utente è obbligatorio."), StringLength(64, ErrorMessage = "L'utente supera i 64 caratteri.")] string Username,
    [property: StringLength(256, ErrorMessage = "La password supera i 256 caratteri.")] string? Password = null,
    bool UseTls = false,
    [property: Range(1, 65535, ErrorMessage = "Porta tra 1 e 65535.")] int Port = 8728,
    bool VerifyCertificate = false,
    [property: StringLength(512)] string? Description = null);

/// <summary>Profilo predefinito dei device con API RouterOS abilitata; null = nessuno (quei device non vengono letti).</summary>
public sealed record RouterOsDefaultDto(Guid? CredentialId);

public enum RouterOsWatchKindDto { Interface, WireGuardPeer }

/// <summary>Elemento sorvegliato con lo stato dell'ultima lettura (Unknown finché non c'è una lettura).</summary>
public sealed record RouterOsWatchDto(RouterOsWatchKindDto Kind, string Key, string Label, NodeState State, DateTimeOffset? Since, string? Detail);

/// <summary>Interfacce e peer dell'ultima lettura di un router e quelli sorvegliati (pannello del dispositivo).</summary>
public sealed record DeviceRouterOsDetailDto(
    Guid DeviceId,
    RouterOsSampleDto? Latest,
    IReadOnlyList<RouterOsWatchDto> Watches);

/// <summary>Attiva o disattiva la sorveglianza di un'interfaccia (Key = nome) o di un peer WireGuard (Key = chiave pubblica).</summary>
public sealed record RouterOsWatchToggleDto(
    RouterOsWatchKindDto Kind,
    [property: Required, StringLength(128)] string Key,
    [property: Required, StringLength(256)] string Label,
    bool Watched);
