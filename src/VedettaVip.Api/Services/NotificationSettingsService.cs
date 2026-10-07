// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.EntityFrameworkCore;
using VedettaVip.Api.Data;
using VedettaVip.Api.Data.Entities;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Services;

/// <summary>Lettura delle impostazioni dei canali con i segreti decifrati (solo in memoria).</summary>
public sealed class NotificationSettingsService(SecretProtector secrets, ILogger<NotificationSettingsService> logger)
{
    public async Task<NotificationSettings> LoadAsync(VedettaVipDbContext db, CancellationToken ct) =>
        await db.NotificationSettings.FirstOrDefaultAsync(s => s.Id == NotificationSettings.SingletonId, ct)
        ?? throw new InvalidOperationException("Riga NotificationSettings mancante: applicare le migration.");

    public static NotificationSettingsDto ToDto(NotificationSettings s) => new(
        s.EmailEnabled, s.SmtpHost, s.SmtpPort, s.SmtpSecurity, s.SmtpUsername, !string.IsNullOrEmpty(s.SmtpPasswordProtected),
        s.SmtpFromAddress, s.SmtpFromName, s.SmtpValidateCertificate, s.TelegramEnabled,
        !string.IsNullOrEmpty(s.TelegramBotTokenProtected), s.DelaySeconds, s.TimeZoneId, s.PublicUrl,
        s.ReminderMinutes, s.ReminderIncludeWarnings);

    /// <summary>Configurazione SMTP pronta all'uso, oppure il motivo per cui non lo è.</summary>
    public (SmtpConfig? Config, string? Problem) Smtp(NotificationSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.SmtpHost)) return (null, "server SMTP non configurato");
        if (string.IsNullOrWhiteSpace(s.SmtpFromAddress)) return (null, "mittente non configurato");

        var password = secrets.Unprotect(s.SmtpPasswordProtected);
        if (!string.IsNullOrEmpty(s.SmtpUsername) && password is null && !string.IsNullOrEmpty(s.SmtpPasswordProtected))
            return (null, "password SMTP non decifrabile: reinserirla in Impostazioni");

        return (new SmtpConfig(s.SmtpHost, s.SmtpPort, s.SmtpSecurity, s.SmtpUsername, password,
            s.SmtpFromAddress, s.SmtpFromName, s.SmtpValidateCertificate), null);
    }

    public (string? Token, string? Problem) Telegram(NotificationSettings s)
    {
        if (string.IsNullOrEmpty(s.TelegramBotTokenProtected)) return (null, "token del bot Telegram non configurato");
        var token = secrets.Unprotect(s.TelegramBotTokenProtected);
        return token is null ? (null, "token Telegram non decifrabile: reinserirlo in Impostazioni") : (token, null);
    }

    /// <summary>Fuso orario per gli orari nei messaggi; UTC se l'id non esiste sul sistema (es. container senza tzdata).</summary>
    public TimeZoneInfo TimeZone(NotificationSettings s)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(s.TimeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger.LogWarning("Fuso orario {TimeZone} non disponibile, uso UTC", s.TimeZoneId);
            return TimeZoneInfo.Utc;
        }
    }
}
