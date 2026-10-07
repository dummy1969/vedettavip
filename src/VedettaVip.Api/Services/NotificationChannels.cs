// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Net.Http.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Services;

/// <summary>Configurazione SMTP con la password già decifrata (solo in memoria, mai loggata).</summary>
public sealed record SmtpConfig(
    string Host, int Port, SmtpSecurity Security, string? Username, string? Password,
    string FromAddress, string? FromName, bool ValidateCertificate);

/// <summary>
/// Invio email con MailKit: SMTP in chiaro, STARTTLS, TLS implicito (SMTPS) o automatico; autenticazione se è
/// impostato un utente (il meccanismo, PLAIN/LOGIN/CRAM-MD5…, lo negozia MailKit con il server).
/// </summary>
public sealed class EmailSender(ILogger<EmailSender> logger)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public async Task SendAsync(SmtpConfig config, string to, string subject, string body, CancellationToken ct)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(config.FromName ?? "VedettaVip", config.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = new SmtpClient { Timeout = (int)Timeout.TotalMilliseconds };
        if (!config.ValidateCertificate)
            client.ServerCertificateValidationCallback = (_, _, _, _) => true; // relay interni con certificato autofirmato

        await client.ConnectAsync(config.Host, config.Port, ToSocketOptions(config.Security), ct);
        if (!string.IsNullOrEmpty(config.Username))
            await client.AuthenticateAsync(config.Username, config.Password ?? "", ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);

        logger.LogInformation("Email inviata a {To}: {Subject}", to, subject);
    }

    private static SecureSocketOptions ToSocketOptions(SmtpSecurity security) => security switch
    {
        SmtpSecurity.None => SecureSocketOptions.None,
        SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        _ => SecureSocketOptions.Auto
    };
}

/// <summary>
/// Invio Telegram con la Bot API (sendMessage). Il token è nell'URL: l'HttpClient di questo servizio è registrato
/// senza logger (Program.cs), altrimenti il token finirebbe nei log in chiaro.
/// </summary>
public sealed class TelegramSender(HttpClient http, ILogger<TelegramSender> logger)
{
    public async Task SendAsync(string botToken, string chatId, string text, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync($"https://api.telegram.org/bot{botToken}/sendMessage",
            new { chat_id = chatId, text, disable_web_page_preview = true }, ct);

        if (!response.IsSuccessStatusCode)
        {
            // Telegram risponde {"ok":false,"description":"Bad Request: chat not found"}: il messaggio è utile all'utente
            var error = await response.Content.ReadFromJsonAsync<TelegramError>(ct);
            throw new InvalidOperationException($"Telegram {(int)response.StatusCode}: {error?.Description ?? response.ReasonPhrase}");
        }

        logger.LogInformation("Messaggio Telegram inviato alla chat {ChatId}", chatId);
    }

    private sealed record TelegramError(bool Ok, string? Description);
}
