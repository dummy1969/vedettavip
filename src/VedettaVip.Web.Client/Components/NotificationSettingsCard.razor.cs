// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using VedettaVip.Shared.Contracts;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Components;

/// <summary>
/// Impostazioni dei canali di notifica. Password SMTP e token Telegram non vengono mai riletti: si possono solo
/// sostituire (campo compilato) o rimuovere; campo vuoto = invariato.
/// </summary>
public partial class NotificationSettingsCard : IDisposable
{
    [Inject] private VedettaVipApiClient Api { get; set; } = default!;

    private readonly CancellationTokenSource cts = new();
    private bool loaded;
    private bool busy;
    private string? loadError;
    private string? message;
    private bool messageIsError;

    private bool emailEnabled;
    private string? smtpHost;
    private int smtpPort;
    private SmtpSecurity smtpSecurity;
    private string? smtpUsername;
    private string? smtpPassword;
    private bool hasSmtpPassword;
    private bool removeSmtpPassword;
    private string? smtpFromAddress;
    private string? smtpFromName;
    private bool smtpValidateCertificate;
    private string? testEmailTo;

    private bool telegramEnabled;
    private string? telegramToken;
    private bool hasTelegramToken;
    private string? testChatId;

    private int delaySeconds;
    private string timeZoneId = "Europe/Rome";
    private string? publicUrl;
    private int reminderMinutes;
    private bool reminderIncludeWarnings;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            Load(await Api.GetNotificationSettingsAsync(cts.Token));
            loaded = true;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (HttpRequestException ex)
        {
            loadError = $"Impostazioni non caricate: {ex.Message}";
        }
    }

    private void Load(NotificationSettingsDto s)
    {
        (emailEnabled, smtpHost, smtpPort, smtpSecurity, smtpUsername, hasSmtpPassword) =
            (s.EmailEnabled, s.SmtpHost, s.SmtpPort, s.SmtpSecurity, s.SmtpUsername, s.HasSmtpPassword);
        (smtpFromAddress, smtpFromName, smtpValidateCertificate) = (s.SmtpFromAddress, s.SmtpFromName, s.SmtpValidateCertificate);
        (telegramEnabled, hasTelegramToken) = (s.TelegramEnabled, s.HasTelegramToken);
        (delaySeconds, timeZoneId, publicUrl) = (s.DelaySeconds, s.TimeZoneId, s.PublicUrl);
        (reminderMinutes, reminderIncludeWarnings) = (s.ReminderMinutes, s.ReminderIncludeWarnings);
        smtpPassword = null;
        telegramToken = null;
        removeSmtpPassword = false;
    }

    /// <summary>Porta tipica della modalità scelta, solo se l'utente non ne ha impostata una diversa da quelle standard.</summary>
    private void OnSecurityChanged()
    {
        if (smtpPort is 25 or 465 or 587)
            smtpPort = smtpSecurity switch
            {
                SmtpSecurity.SslOnConnect => 465,
                SmtpSecurity.None => 25,
                SmtpSecurity.StartTls => 587,
                _ => smtpPort
            };
    }

    private async Task<bool> SaveCoreAsync()
    {
        var dto = new NotificationSettingsUpdateDto(
            emailEnabled, smtpHost, smtpPort, smtpSecurity, smtpUsername,
            removeSmtpPassword ? "" : string.IsNullOrEmpty(smtpPassword) ? null : smtpPassword,
            smtpFromAddress, smtpFromName, smtpValidateCertificate,
            telegramEnabled, string.IsNullOrWhiteSpace(telegramToken) ? null : telegramToken.Trim(),
            delaySeconds, timeZoneId, publicUrl, reminderMinutes, reminderIncludeWarnings);

        Load(await Api.UpdateNotificationSettingsAsync(dto, cts.Token));
        return true;
    }

    private Task SaveAsync() => RunAsync(async () =>
    {
        await SaveCoreAsync();
        return $"Salvate alle {DateTime.Now:HH:mm:ss}.";
    });

    private Task TestEmailAsync() => RunAsync(async () =>
    {
        await SaveCoreAsync();
        await Api.SendTestEmailAsync(testEmailTo!.Trim(), cts.Token);
        return $"Email di prova inviata a {testEmailTo}: controlla la casella (anche lo spam).";
    });

    private Task TestTelegramAsync() => RunAsync(async () =>
    {
        await SaveCoreAsync();
        await Api.SendTestTelegramAsync(testChatId!.Trim(), cts.Token);
        return $"Messaggio di prova inviato alla chat {testChatId}.";
    });

    private async Task RunAsync(Func<Task<string>> action)
    {
        busy = true;
        message = null;
        try
        {
            message = await action();
            messageIsError = false;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (HttpRequestException ex)
        {
            message = ex.Message; // errore di validazione o risposta del server SMTP/Telegram
            messageIsError = true;
        }
        finally
        {
            busy = false;
        }
    }

    public void Dispose()
    {
        cts.Cancel();
        cts.Dispose();
    }
}
