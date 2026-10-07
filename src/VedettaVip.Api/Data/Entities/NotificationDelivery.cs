// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Data.Entities;

/// <summary>
/// Invio di un evento a un contatto su un canale, con tentativi ed esito. Nome e destinazione sono copiati al
/// momento della creazione: lo storico resta leggibile anche se il contatto viene modificato o eliminato.
/// </summary>
public sealed class NotificationDelivery
{
    public long Id { get; set; }
    public long EventId { get; set; }
    public Event? Event { get; set; }
    public Guid? ContactId { get; set; }
    public required string ContactName { get; set; }
    public NotificationChannel Channel { get; set; }
    public required string Destination { get; set; }
    public DeliveryStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    /// <summary>Promemoria di un problema ancora aperto (testo "PROMEMORIA").</summary>
    public bool IsReminder { get; set; }
}
