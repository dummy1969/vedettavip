// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.ComponentModel.DataAnnotations;
using VedettaVip.Shared.Models;

namespace VedettaVip.Shared.Contracts;

/// <summary>Ambito di un'iscrizione: tutti gli eventi, i device di un cliente, i device di una mappa (e sottomappe).</summary>
public enum SubscriptionScope { All, Customer, Map }

/// <summary>Quali problemi notificare: solo Down, anche Partial (SNMP non risponde), oppure anche le soglie superate.</summary>
public enum AlertFilter { DownOnly, DownAndPartial, AllProblems }

/// <summary>
/// Stato della notifica di un evento (l'evento è l'outbox). Maintenance: rimandata perché il device era in manutenzione;
/// alla fine della finestra parte se il problema è ancora aperto, altrimenti diventa Suppressed.
/// </summary>
public enum NotifyState { None, Pending, Done, Suppressed, Maintenance }

public enum NotificationChannel { Email, Telegram }

public enum DeliveryStatus { Pending, Sent, Failed }

/// <summary>Sicurezza della connessione SMTP (MailKit SecureSocketOptions).</summary>
public enum SmtpSecurity
{
    /// <summary>TLS implicito sulla 465, STARTTLS se il server lo offre altrimenti in chiaro.</summary>
    Auto,
    /// <summary>Nessuna cifratura (relay interno, porta 25).</summary>
    None,
    /// <summary>STARTTLS obbligatorio (tipicamente porta 587).</summary>
    StartTls,
    /// <summary>TLS implicito dalla connessione (SMTPS, tipicamente porta 465).</summary>
    SslOnConnect
}

public sealed record CustomerDto(Guid Id, string Name, string? Notes, int DeviceCount, Guid? SnmpCredentialId = null,
    Guid? RouterOsCredentialId = null);

/// <summary>Attenzione: il PUT sostituisce tutto, anche il profilo SNMP (null = predefinito).</summary>
public sealed record CustomerUpsertDto(
    [property: Required(ErrorMessage = "Il nome è obbligatorio."), StringLength(128)] string Name,
    [property: StringLength(1024)] string? Notes = null,
    Guid? SnmpCredentialId = null,
    Guid? RouterOsCredentialId = null);

public sealed record SubscriptionDto(
    Guid Id,
    SubscriptionScope Scope,
    Guid? CustomerId,
    Guid? MapId,
    string? ScopeName,
    AlertFilter Filter,
    bool NotifyRecovery);

public sealed record SubscriptionUpsertDto(
    SubscriptionScope Scope,
    Guid? CustomerId = null,
    Guid? MapId = null,
    AlertFilter Filter = AlertFilter.DownOnly,
    bool NotifyRecovery = true);

public sealed record ContactDto(
    Guid Id,
    string Name,
    string? Email,
    string? TelegramChatId,
    bool Enabled,
    IReadOnlyList<SubscriptionDto> Subscriptions);

/// <summary>Contatto con le sue iscrizioni (sostituite per intero a ogni salvataggio). Serve almeno un canale.</summary>
public sealed record ContactUpsertDto(
    [property: Required(ErrorMessage = "Il nome è obbligatorio."), StringLength(128)] string Name,
    [property: StringLength(256), EmailAddress(ErrorMessage = "Indirizzo email non valido.")] string? Email,
    [property: StringLength(64)] string? TelegramChatId,
    bool Enabled = true,
    IReadOnlyList<SubscriptionUpsertDto>? Subscriptions = null);

/// <summary>Impostazioni dei canali. I segreti non escono mai: solo HasSmtpPassword/HasTelegramToken.</summary>
public sealed record NotificationSettingsDto(
    bool EmailEnabled,
    string? SmtpHost,
    int SmtpPort,
    SmtpSecurity SmtpSecurity,
    string? SmtpUsername,
    bool HasSmtpPassword,
    string? SmtpFromAddress,
    string? SmtpFromName,
    bool SmtpValidateCertificate,
    bool TelegramEnabled,
    bool HasTelegramToken,
    int DelaySeconds,
    string TimeZoneId,
    string? PublicUrl,
    int ReminderMinutes = 0,
    bool ReminderIncludeWarnings = false);

/// <summary>
/// Aggiornamento delle impostazioni. SmtpPassword e TelegramBotToken: null = invariato, stringa vuota = rimuovi,
/// altro = nuovo valore (salvato cifrato con Data Protection).
/// </summary>
public sealed record NotificationSettingsUpdateDto(
    bool EmailEnabled,
    [property: StringLength(255)] string? SmtpHost,
    [property: Range(1, 65535, ErrorMessage = "Porta SMTP tra 1 e 65535.")] int SmtpPort,
    SmtpSecurity SmtpSecurity,
    [property: StringLength(256)] string? SmtpUsername,
    [property: StringLength(256)] string? SmtpPassword,
    [property: StringLength(256), EmailAddress(ErrorMessage = "Mittente non valido.")] string? SmtpFromAddress,
    [property: StringLength(128)] string? SmtpFromName,
    bool SmtpValidateCertificate,
    bool TelegramEnabled,
    [property: StringLength(256)] string? TelegramBotToken,
    [property: Range(0, 3600, ErrorMessage = "Ritardo tra 0 e 3600 secondi.")] int DelaySeconds,
    [property: Required, StringLength(64)] string TimeZoneId,
    [property: StringLength(512), Url(ErrorMessage = "Indirizzo non valido (es. https://vedettavip.example.com).")] string? PublicUrl,
    [property: Range(0, 10_080, ErrorMessage = "Promemoria: tra 0 (disattivati) e 10080 minuti.")] int ReminderMinutes = 0,
    bool ReminderIncludeWarnings = false);

public sealed record TestEmailDto([property: Required, EmailAddress(ErrorMessage = "Indirizzo email non valido.")] string To);

public sealed record TestTelegramDto([property: Required, StringLength(64)] string ChatId);

/// <summary>Evento per la pagina Eventi.</summary>
public sealed record EventDto(
    long Id,
    DateTimeOffset Time,
    EventSeverity Severity,
    string Type,
    string Message,
    Guid? DeviceId,
    string? DeviceName,
    string? AgentId,
    string? CustomerName,
    NodeState? FromState,
    NodeState? ToState,
    bool Acknowledged,
    DateTimeOffset? ResolvedAt,
    string? AcknowledgedBy,
    DateTimeOffset? AcknowledgedAt,
    NotifyState NotifyState,
    string? NotifyNote,
    int DeliveriesSent,
    int DeliveriesPending,
    int DeliveriesFailed);

public sealed record DeliveryDto(
    long Id,
    NotificationChannel Channel,
    string ContactName,
    string Destination,
    DeliveryStatus Status,
    int Attempts,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt);

/// <summary>Presa in carico (o rilascio) di un evento: PUT /api/events/{id}/ack.</summary>
public sealed record EventAckDto(bool Acknowledged);
