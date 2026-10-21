// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.EntityFrameworkCore;
using VedettaVip.Api.Data;
using VedettaVip.Api.Data.Entities;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Services;

/// <summary>
/// Regole (pure, coperte da test) dello stato di un canale di notifica: un canale è in errore quando dopo l'ultimo
/// invio riuscito su quel canale ci sono almeno <see cref="FailingAfterAttempts"/> tentativi falliti. Esempio: server
/// SMTP irraggiungibile o DNS dei container vuoto dopo un riavvio (ottobre 2026: due giorni di email perse senza
/// che nessuno se ne accorgesse). Un invio fallito creato prima dell'ultimo riuscito non conta: riguarda il destinatario,
/// non il canale.
/// </summary>
public static class NotificationHealthRules
{
    public const int FailingAfterAttempts = 3;

    /// <summary>Invio non riuscito (in ritentativo o fallito definitivamente) con almeno un tentativo.</summary>
    public sealed record FailedDelivery(long Id, DateTimeOffset CreatedAt, int Attempts, string? LastError);

    public static NotificationChannelHealthDto Evaluate(
        NotificationChannel channel, DateTimeOffset? lastSentAt, IEnumerable<FailedDelivery> failures)
    {
        var recent = failures.Where(f => f.Attempts > 0 && (lastSentAt is null || f.CreatedAt > lastSentAt)).ToList();
        var attempts = recent.Sum(f => f.Attempts);
        var failing = attempts >= FailingAfterAttempts;
        return new NotificationChannelHealthDto(channel, failing,
            failing ? recent.Min(f => f.CreatedAt) : null,
            attempts, recent.Count,
            failing ? recent.MaxBy(f => f.Id)?.LastError : null,
            lastSentAt);
    }

    /// <summary>AlertKey degli eventi NotificationsFailing / NotificationsRestored: "notify:Email", "notify:Telegram".</summary>
    public static string AlertKey(NotificationChannel channel) => $"notify:{channel}";

    /// <summary>Canale di un AlertKey "notify:…", null per gli altri.</summary>
    public static NotificationChannel? ChannelOf(string? alertKey) =>
        alertKey is not null && alertKey.StartsWith("notify:", StringComparison.Ordinal)
        && Enum.TryParse<NotificationChannel>(alertKey["notify:".Length..], out var channel)
            ? channel
            : null;
}

/// <summary>Lettura dello stato dei canali attivi dal database (consegne di NotificationDeliveries).</summary>
public static class NotificationHealth
{
    /// <summary>Al massimo questi invii non riusciti per canale (i più recenti): bastano per decidere e per il conteggio.</summary>
    private const int MaxFailures = 1000;

    public static IEnumerable<NotificationChannel> EnabledChannels(NotificationSettings settings)
    {
        if (settings.EmailEnabled) yield return NotificationChannel.Email;
        if (settings.TelegramEnabled) yield return NotificationChannel.Telegram;
    }

    /// <summary>Stato dei soli canali attivi nelle impostazioni.</summary>
    public static async Task<List<NotificationChannelHealthDto>> LoadAsync(
        VedettaVipDbContext db, NotificationSettings settings, CancellationToken ct)
    {
        var result = new List<NotificationChannelHealthDto>();
        foreach (var channel in EnabledChannels(settings))
            result.Add(await LoadAsync(db, channel, ct));
        return result;
    }

    public static async Task<NotificationChannelHealthDto> LoadAsync(VedettaVipDbContext db, NotificationChannel channel, CancellationToken ct)
    {
        var deliveries = db.NotificationDeliveries.AsNoTracking().Where(d => d.Channel == channel);
        var lastSent = await deliveries.Where(d => d.Status == DeliveryStatus.Sent).MaxAsync(d => d.SentAt, ct);
        var failures = await deliveries
            .Where(d => d.Status != DeliveryStatus.Sent && d.Attempts > 0 && (lastSent == null || d.CreatedAt > lastSent))
            .OrderByDescending(d => d.Id)
            .Take(MaxFailures)
            .Select(d => new NotificationHealthRules.FailedDelivery(d.Id, d.CreatedAt, d.Attempts, d.LastError))
            .ToListAsync(ct);
        return NotificationHealthRules.Evaluate(channel, lastSent, failures);
    }
}
