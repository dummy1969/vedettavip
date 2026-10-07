// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Data.Entities;

/// <summary>
/// Impostazioni dei canali di notifica, una sola riga (Id = 1). Password SMTP e token Telegram sono cifrati
/// con ASP.NET Core Data Protection (<c>Services/SecretProtector</c>): mai in chiaro nel database.
/// </summary>
public sealed class NotificationSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public bool EmailEnabled { get; set; }
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; }
    public SmtpSecurity SmtpSecurity { get; set; }
    public string? SmtpUsername { get; set; }
    public string? SmtpPasswordProtected { get; set; }
    public string? SmtpFromAddress { get; set; }
    public string? SmtpFromName { get; set; }
    public bool SmtpValidateCertificate { get; set; }

    public bool TelegramEnabled { get; set; }
    public string? TelegramBotTokenProtected { get; set; }

    /// <summary>Attesa prima di notificare un evento: serve a sopprimere gli avvisi dei device dietro un padre giù.</summary>
    public int DelaySeconds { get; set; }
    /// <summary>Fuso orario degli orari nei messaggi (IANA, es. Europe/Rome).</summary>
    public required string TimeZoneId { get; set; }
    /// <summary>Indirizzo di VedettaVip per i link nei messaggi (facoltativo).</summary>
    public string? PublicUrl { get; set; }

    /// <summary>Promemoria per i problemi aperti (né presi in carico né risolti) ogni N minuti; 0 = disattivati.</summary>
    public int ReminderMinutes { get; set; }
    /// <summary>Promemoria anche per Partial e soglie (altrimenti solo Down e agenti offline).</summary>
    public bool ReminderIncludeWarnings { get; set; }
}
