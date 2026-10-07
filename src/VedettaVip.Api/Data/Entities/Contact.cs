// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Data.Entities;

/// <summary>Destinatario delle notifiche: almeno un canale tra email e chat Telegram.</summary>
public sealed class Contact
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Email { get; set; }
    /// <summary>Chat ID Telegram (numerico, negativo per i gruppi) o @nomecanale.</summary>
    public string? TelegramChatId { get; set; }
    public bool Enabled { get; set; } = true;
    public List<Subscription> Subscriptions { get; set; } = [];
}

/// <summary>
/// Iscrizione di un contatto: ambito (tutto, cliente, mappa con sottomappe), problemi da notificare
/// (solo Down o anche Partial) e se avvisare al ripristino.
/// </summary>
public sealed class Subscription
{
    public Guid Id { get; set; }
    public Guid ContactId { get; set; }
    public Contact? Contact { get; set; }
    public SubscriptionScope Scope { get; set; }
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public Guid? MapId { get; set; }
    public Map? Map { get; set; }
    public AlertFilter Filter { get; set; }
    public bool NotifyRecovery { get; set; }
}
