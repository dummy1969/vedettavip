// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.EntityFrameworkCore;
using VedettaVip.Api.Data;
using VedettaVip.Api.Data.Entities;
using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;

namespace VedettaVip.Api.Services;

/// <summary>
/// Motore delle notifiche (outbox). Ogni 5 secondi:
/// <list type="number">
/// <item><b>fine manutenzione</b>: gli eventi rimandati (NotifyState = Maintenance) tornano Pending se il problema è
/// ancora aperto e la finestra è finita, oppure diventano Suppressed se nel frattempo è stato risolto o preso in carico;</item>
/// <item><b>preparazione</b> degli eventi Pending scaduti: manutenzione, dipendenze, flap, destinatari; una consegna
/// per contatto e canale (NotificationDeliveries);</item>
/// <item><b>promemoria</b> per i problemi ancora aperti, se attivati in Impostazioni;</item>
/// <item><b>invio</b> delle consegne con ritentativi e backoff (<see cref="AlertRules.RetryDelay"/>).</item>
/// </list>
/// Tutto passa dal database: un riavvio dell'API o un server SMTP giù non fanno perdere avvisi.
/// Con più istanze dell'API servirà un lock (FOR UPDATE SKIP LOCKED).
/// </summary>
public sealed class NotificationDispatcher(
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<NotificationDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private const int EventBatch = 100;
    private const int DeliveryBatch = 20;

    private sealed record DeviceInfo(Guid Id, string Name, string Address, Guid? ParentId, Guid? CustomerId, NodeState State);

    /// <summary>Dati di instradamento caricati una volta per giro: device, mappe, iscrizioni, finestre attive.</summary>
    private sealed class Routing
    {
        public required Dictionary<Guid, DeviceInfo> Devices { get; init; }
        public required Dictionary<Guid, Guid?> MapParents { get; init; }
        public required ILookup<Guid, Guid> DeviceMaps { get; init; }
        public required List<Subscription> Subscriptions { get; init; }
        public required List<MaintenanceWindow> ActiveWindows { get; init; }

        /// <summary>Mappe che contengono il device, con le mappe superiori (iscrizioni e manutenzione per mappa).</summary>
        public HashSet<Guid> MapsOf(Guid deviceId)
        {
            var result = new HashSet<Guid>();
            foreach (var map in DeviceMaps[deviceId])
            {
                Guid? current = map;
                while (current is { } id && result.Add(id))
                    current = MapParents.GetValueOrDefault(id);
            }
            return result;
        }

        /// <summary>Finestra di manutenzione attiva che copre il device (null = nessuna). Per gli agenti solo quelle "Tutto".</summary>
        public MaintenanceWindow? MaintenanceFor(Guid? deviceId)
        {
            if (deviceId is not { } id || !Devices.TryGetValue(id, out var device))
                return ActiveWindows.FirstOrDefault(w => w.Scope == MaintenanceScope.All);

            var maps = MapsOf(id);
            return ActiveWindows.FirstOrDefault(w =>
                MaintenanceSchedule.Covers(w.Scope, w.CustomerId, w.MapId, w.DeviceId, id, device.CustomerId, maps));
        }

        /// <summary>Contatti che devono ricevere l'avviso (un contatto una volta sola).</summary>
        public List<Contact> Recipients(Event ev, AlertKind kind)
        {
            var device = ev.DeviceId is { } id ? Devices.GetValueOrDefault(id) : null;
            var maps = device is null ? [] : MapsOf(device.Id);
            return Subscriptions
                .Where(s => device is null
                    ? s.Scope == SubscriptionScope.All // eventi degli agenti: solo chi è iscritto a tutto
                    : s.Scope == SubscriptionScope.All
                      || (s.Scope == SubscriptionScope.Customer && s.CustomerId == device.CustomerId)
                      || (s.Scope == SubscriptionScope.Map && s.MapId is { } m && maps.Contains(m)))
                .Where(s => AlertRules.Qualifies(s.Filter, s.NotifyRecovery, kind, ev.FromState))
                .Select(s => s.Contact!)
                .DistinctBy(c => c.Id)
                .ToList();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<VedettaVipDbContext>();
                    var settingsService = scope.ServiceProvider.GetRequiredService<NotificationSettingsService>();

                    var settings = await settingsService.LoadAsync(db, stoppingToken);
                    var routing = await LoadRoutingAsync(db, settingsService.TimeZone(settings), stoppingToken);

                    await ReleaseMaintenanceAsync(db, routing, stoppingToken);
                    await PrepareDueEventsAsync(db, settings, routing, stoppingToken);
                    await SendRemindersAsync(db, settings, routing, stoppingToken);
                    await SendDueDeliveriesAsync(scope.ServiceProvider, db, settingsService, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Database giù o errore inatteso: si riprova al prossimo giro, nulla è perso
                    logger.LogError(ex, "Ciclo del dispatcher delle notifiche fallito");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    private async Task<Routing> LoadRoutingAsync(VedettaVipDbContext db, TimeZoneInfo zone, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var windows = await db.MaintenanceWindows.AsNoTracking().Where(w => w.Enabled).ToListAsync(ct);
        return new Routing
        {
            Devices = await LoadDevicesAsync(db, ct),
            MapParents = await db.Maps.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.ParentMapId, ct),
            DeviceMaps = (await db.MapNodes.AsNoTracking()
                    .Where(n => n.DeviceId != null)
                    .Select(n => new { DeviceId = n.DeviceId!.Value, n.MapId })
                    .ToListAsync(ct))
                .ToLookup(x => x.DeviceId, x => x.MapId),
            Subscriptions = await db.Subscriptions.AsNoTracking().Include(s => s.Contact).Where(s => s.Contact!.Enabled).ToListAsync(ct),
            ActiveWindows = windows.Where(w => MaintenanceSchedule.IsActive(ToSchedule(w), now, zone)).ToList()
        };
    }

    // ---------- 1. Fine della manutenzione ----------

    private async Task ReleaseMaintenanceAsync(VedettaVipDbContext db, Routing routing, CancellationToken ct)
    {
        var deferred = await db.Events.Where(e => e.NotifyState == NotifyState.Maintenance).ToListAsync(ct);
        foreach (var ev in deferred)
        {
            if (ev.Acknowledged || ev.ResolvedAt is not null)
                await FinishAsync(db, ev, NotifyState.Suppressed, ev.ResolvedAt is not null
                    ? "risolto durante la manutenzione" : "preso in carico durante la manutenzione", ct);
            else if (routing.MaintenanceFor(ev.DeviceId) is null)
            {
                // Finestra finita e problema ancora aperto: si notifica adesso
                ev.NotifyState = NotifyState.Pending;
                ev.NotifyAfter = time.GetUtcNow();
                ev.NotifyNote = "ancora aperto alla fine della manutenzione";
                await db.SaveChangesAsync(ct);
            }
        }
    }

    // ---------- 2. Preparazione: manutenzione, dipendenze, flap, destinatari ----------

    private async Task PrepareDueEventsAsync(VedettaVipDbContext db, NotificationSettings settings, Routing routing, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var due = await db.Events
            .Where(e => e.NotifyState == NotifyState.Pending && e.NotifyAfter <= now)
            .OrderBy(e => e.Time).ThenBy(e => e.Id)
            .Take(EventBatch)
            .ToListAsync(ct);

        foreach (var ev in due)
        {
            var kind = AlertRules.KindOf(ev.Type, ev.FromState, ev.ToState);
            if (kind is null)
            {
                await FinishAsync(db, ev, NotifyState.None, null, ct);
                continue;
            }

            // Manutenzione: i problemi si rimandano alla fine della finestra, i rientri non si notificano
            if (routing.MaintenanceFor(ev.DeviceId) is { } window)
            {
                var problem = AlertRules.IsProblem(kind.Value);
                await FinishAsync(db, ev, problem ? NotifyState.Maintenance : NotifyState.Suppressed, $"in manutenzione: {window.Name}", ct);
                continue;
            }

            if (ev.DeviceId is { } deviceId && await SuppressionReasonAsync(db, ev, kind.Value, deviceId, routing.Devices, ct) is { } reason)
            {
                await FinishAsync(db, ev, NotifyState.Suppressed, reason, ct);
                logger.LogInformation("Avviso dell'evento {EventId} soppresso: {Reason}", ev.Id, reason);
                continue;
            }

            var contacts = routing.Recipients(ev, kind.Value);
            var created = AddDeliveries(db, ev, contacts, settings, isReminder: false, now);
            await FinishAsync(db, ev, NotifyState.Done, created == 1 ? "1 invio" : created > 1 ? $"{created} invii"
                : contacts.Count == 0 ? "nessun destinatario iscritto" : "nessun canale attivo per i destinatari", ct);
        }
    }

    /// <summary>
    /// Motivo per non notificare, oppure null:
    /// <list type="bullet">
    /// <item>Down con un dispositivo da cui dipende (risalendo i padri) a sua volta Down: avvisa solo il padre;</item>
    /// <item>problema rientrato prima della fine del ritardo di invio (flap): non si avvisa né il problema né il rientro;</item>
    /// <item>rientro di un problema il cui avviso non è stato inviato (soppresso, in manutenzione).</item>
    /// </list>
    /// I problemi di stato (Down/Partial → Up) e di soglia (stessa AlertKey) seguono le stesse regole.
    /// </summary>
    private static async Task<string?> SuppressionReasonAsync(
        VedettaVipDbContext db, Event ev, AlertKind kind, Guid deviceId, Dictionary<Guid, DeviceInfo> devices, CancellationToken ct)
    {
        if (kind == AlertKind.Down && DownAncestor(deviceId, devices) is { } parent)
            return $"dipende da {parent.Name}, che è down";

        var sameDevice = db.Events.AsNoTracking().Where(e => e.DeviceId == deviceId);

        if (kind is AlertKind.Down or AlertKind.Partial)
        {
            if (await sameDevice.AnyAsync(e => e.Type == EventTypes.StateChange && e.Time > ev.Time && e.ToState == NodeState.Up, ct))
                return "rientrato entro il ritardo di invio";
        }
        else if (kind == AlertKind.Threshold)
        {
            if (await sameDevice.AnyAsync(e => e.Type == EventTypes.ThresholdCleared && e.AlertKey == ev.AlertKey && e.Time > ev.Time, ct))
                return "rientrato entro il ritardo di invio";
        }

        if (kind is AlertKind.Recovery or AlertKind.ThresholdCleared)
        {
            var problems = kind == AlertKind.Recovery
                ? sameDevice.Where(e => e.Type == EventTypes.StateChange && (e.ToState == NodeState.Down || e.ToState == NodeState.Partial))
                : sameDevice.Where(e => e.Type == EventTypes.ThresholdRaised && e.AlertKey == ev.AlertKey);
            var problem = await problems
                .Where(e => e.Time < ev.Time || (e.Time == ev.Time && e.Id < ev.Id))
                .OrderByDescending(e => e.Time).ThenByDescending(e => e.Id)
                .Select(e => new { e.NotifyState })
                .FirstOrDefaultAsync(ct);
            if (problem?.NotifyState is NotifyState.Suppressed or NotifyState.None or NotifyState.Maintenance)
                return "rientro di un problema non notificato";
        }

        return null;
    }

    private static DeviceInfo? DownAncestor(Guid deviceId, Dictionary<Guid, DeviceInfo> devices)
    {
        var visited = new HashSet<Guid> { deviceId };
        var current = devices.GetValueOrDefault(deviceId)?.ParentId;
        while (current is { } id && visited.Add(id) && devices.TryGetValue(id, out var parent))
        {
            if (parent.State == NodeState.Down)
                return parent;
            current = parent.ParentId;
        }
        return null;
    }

    private static int AddDeliveries(VedettaVipDbContext db, Event ev, List<Contact> contacts, NotificationSettings settings, bool isReminder, DateTimeOffset now)
    {
        var created = 0;
        foreach (var contact in contacts)
        {
            if (settings.EmailEnabled && contact.Email is { } email)
                created += AddDelivery(db, ev, contact, NotificationChannel.Email, email, isReminder, now);
            if (settings.TelegramEnabled && contact.TelegramChatId is { } chat)
                created += AddDelivery(db, ev, contact, NotificationChannel.Telegram, chat, isReminder, now);
        }
        return created;
    }

    private static int AddDelivery(VedettaVipDbContext db, Event ev, Contact contact, NotificationChannel channel, string destination, bool isReminder, DateTimeOffset now)
    {
        db.NotificationDeliveries.Add(new NotificationDelivery
        {
            Event = ev,
            ContactId = contact.Id,
            ContactName = contact.Name,
            Channel = channel,
            Destination = destination,
            Status = DeliveryStatus.Pending,
            NextAttemptAt = now,
            CreatedAt = now,
            IsReminder = isReminder
        });
        return 1;
    }

    /// <summary>
    /// Salva subito l'esito: gli eventi successivi dello stesso giro lo leggono dal database (es. il rientro di un
    /// problema appena soppresso per flap deve vederlo soppresso, non ancora "da notificare").
    /// </summary>
    private static async Task FinishAsync(VedettaVipDbContext db, Event ev, NotifyState state, string? note, CancellationToken ct)
    {
        ev.NotifyState = state;
        ev.NotifyNote = note;
        await db.SaveChangesAsync(ct);
    }

    // ---------- 3. Promemoria ----------

    /// <summary>
    /// Problemi notificati, ancora aperti (né risolti né presi in carico) e non in manutenzione: nuovo invio ogni
    /// ReminderMinutes. Un Down si considera aperto solo se il device è ancora Down; un agente solo se ancora offline.
    /// </summary>
    private async Task SendRemindersAsync(VedettaVipDbContext db, NotificationSettings settings, Routing routing, CancellationToken ct)
    {
        if (settings.ReminderMinutes <= 0)
            return;

        var now = time.GetUtcNow();
        var limit = now - TimeSpan.FromMinutes(settings.ReminderMinutes);
        var candidates = await db.Events
            .Where(e => e.NotifyState == NotifyState.Done && !e.Acknowledged && e.ResolvedAt == null
                        && (e.LastReminderAt ?? e.Time) <= limit
                        && (e.Type == EventTypes.StateChange || e.Type == EventTypes.AgentOffline || e.Type == EventTypes.ThresholdRaised))
            .OrderBy(e => e.Time)
            .Take(EventBatch)
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return;

        var offlineAgents = await db.Agents.AsNoTracking().Where(a => a.OfflineSince != null).Select(a => a.Id).ToListAsync(ct);
        foreach (var ev in candidates)
        {
            var kind = AlertRules.KindOf(ev.Type, ev.FromState, ev.ToState);
            if (kind is null || !AlertRules.RemindAbout(kind.Value, settings.ReminderIncludeWarnings))
                continue;

            var stillOpen = ev.Type switch
            {
                EventTypes.StateChange => ev.DeviceId is { } d && routing.Devices.GetValueOrDefault(d)?.State == ev.ToState,
                EventTypes.AgentOffline => ev.AgentId is { } a && offlineAgents.Contains(a),
                _ => true // soglia: aperta finché non c'è il rientro (ResolvedAt)
            };
            if (!stillOpen || routing.MaintenanceFor(ev.DeviceId) is not null)
                continue;

            var created = AddDeliveries(db, ev, routing.Recipients(ev, kind.Value), settings, isReminder: true, now);
            ev.LastReminderAt = now;
            ev.ReminderCount++;
            await db.SaveChangesAsync(ct);
            if (created > 0)
                logger.LogInformation("Promemoria n. {Count} per l'evento {EventId}", ev.ReminderCount, ev.Id);
        }
    }

    // ---------- 4. Invio con ritentativi ----------

    private async Task SendDueDeliveriesAsync(IServiceProvider services, VedettaVipDbContext db, NotificationSettingsService settingsService, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var due = await db.NotificationDeliveries
            .Include(d => d.Event)
            .Where(d => d.Status == DeliveryStatus.Pending && d.NextAttemptAt <= now)
            .OrderBy(d => d.NextAttemptAt)
            .Take(DeliveryBatch)
            .ToListAsync(ct);
        if (due.Count == 0)
            return;

        var settings = await settingsService.LoadAsync(db, ct);
        var timeZone = settingsService.TimeZone(settings);
        var email = services.GetRequiredService<EmailSender>();
        var telegram = services.GetRequiredService<TelegramSender>();
        var messages = new Dictionary<(long, bool), (string Subject, string Body)>();

        foreach (var delivery in due)
        {
            var ev = delivery.Event!;
            if (!messages.TryGetValue((ev.Id, delivery.IsReminder), out var message))
                messages[(ev.Id, delivery.IsReminder)] = message =
                    AlertMessage.Build(await BuildContextAsync(db, ev, settings, timeZone, delivery.IsReminder, now, ct));

            delivery.Attempts++;
            try
            {
                switch (delivery.Channel)
                {
                    case NotificationChannel.Email:
                        if (!settings.EmailEnabled) throw new InvalidOperationException("canale email disattivato");
                        var (smtp, smtpProblem) = settingsService.Smtp(settings);
                        if (smtp is null) throw new InvalidOperationException(smtpProblem);
                        await email.SendAsync(smtp, delivery.Destination, message.Subject, message.Body, ct);
                        break;

                    case NotificationChannel.Telegram:
                        if (!settings.TelegramEnabled) throw new InvalidOperationException("canale Telegram disattivato");
                        var (token, tokenProblem) = settingsService.Telegram(settings);
                        if (token is null) throw new InvalidOperationException(tokenProblem);
                        await telegram.SendAsync(token, delivery.Destination, $"{message.Subject}\n\n{message.Body}", ct);
                        break;
                }

                delivery.Status = DeliveryStatus.Sent;
                delivery.SentAt = time.GetUtcNow();
                delivery.LastError = null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                delivery.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                if (delivery.Attempts >= AlertRules.MaxAttempts)
                {
                    delivery.Status = DeliveryStatus.Failed;
                    logger.LogError("Notifica {Id} ({Channel} a {Destination}) fallita definitivamente: {Error}",
                        delivery.Id, delivery.Channel, delivery.Destination, delivery.LastError);
                }
                else
                {
                    delivery.NextAttemptAt = time.GetUtcNow() + AlertRules.RetryDelay(delivery.Attempts);
                    logger.LogWarning("Notifica {Id} ({Channel} a {Destination}) non inviata, tentativo {Attempt}: {Error}",
                        delivery.Id, delivery.Channel, delivery.Destination, delivery.Attempts, delivery.LastError);
                }
            }

            // Salvataggio a ogni invio: un riavvio a metà non fa ripartire messaggi già spediti
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task<AlertContext> BuildContextAsync(
        VedettaVipDbContext db, Event ev, NotificationSettings settings, TimeZoneInfo timeZone, bool isReminder, DateTimeOffset now, CancellationToken ct)
    {
        var kind = AlertRules.KindOf(ev.Type, ev.FromState, ev.ToState) ?? AlertKind.Down;
        var baseUrl = settings.PublicUrl;
        TimeSpan? openFor = isReminder ? now - ev.Time : null;

        if (ev.DeviceId is not { } deviceId)
            return new AlertContext(kind, ev.AgentId ?? "?", null, null, [], ev.Time, timeZone, null, openFor, [],
                baseUrl is null ? null : $"{baseUrl}/events", ev.Message, isReminder);

        var device = await db.Devices.AsNoTracking()
            .Where(d => d.Id == deviceId)
            .Select(d => new { d.Name, d.Address, Customer = d.Customer != null ? d.Customer.Name : null })
            .FirstOrDefaultAsync(ct);
        var maps = await db.MapNodes.AsNoTracking()
            .Where(n => n.DeviceId == deviceId)
            .OrderBy(n => n.Map!.Name)
            .Select(n => new { n.MapId, n.Map!.Name })
            .ToListAsync(ct);

        var duration = openFor;
        if (!isReminder && kind is AlertKind.Recovery or AlertKind.ThresholdCleared)
        {
            var problems = kind == AlertKind.Recovery
                ? db.Events.Where(e => e.DeviceId == deviceId && e.Type == EventTypes.StateChange
                                       && (e.ToState == NodeState.Down || e.ToState == NodeState.Partial))
                : db.Events.Where(e => e.DeviceId == deviceId && e.Type == EventTypes.ThresholdRaised && e.AlertKey == ev.AlertKey);
            var problemTime = await problems.AsNoTracking()
                .Where(e => e.Time < ev.Time)
                .OrderByDescending(e => e.Time)
                .Select(e => (DateTimeOffset?)e.Time)
                .FirstOrDefaultAsync(ct);
            duration = ev.Time - problemTime;
        }

        // Per un Down: i dispositivi che dipendono da questo (a qualsiasi livello) e sono giù
        var dependents = kind == AlertKind.Down ? await UnreachableDescendantsAsync(db, deviceId, ct) : [];

        var link = baseUrl is null ? null : maps.Count > 0 ? $"{baseUrl}/map/{maps[0].MapId}" : $"{baseUrl}/devices";
        return new AlertContext(kind, device?.Name ?? "?", device?.Address, device?.Customer, maps.Select(m => m.Name).ToList(),
            ev.Time, timeZone, ev.FromState, duration, dependents, link, ev.Message, isReminder, ev.AlertKey);
    }

    private static async Task<List<string>> UnreachableDescendantsAsync(VedettaVipDbContext db, Guid deviceId, CancellationToken ct)
    {
        var all = await LoadDevicesAsync(db, ct);
        var children = all.Values.Where(d => d.ParentId is not null).ToLookup(d => d.ParentId!.Value);
        var result = new List<string>();
        var visited = new HashSet<Guid> { deviceId };
        var queue = new Queue<Guid>([deviceId]);
        while (queue.TryDequeue(out var id))
            foreach (var child in children[id].Where(c => visited.Add(c.Id)))
            {
                if (child.State == NodeState.Down)
                    result.Add(child.Name);
                queue.Enqueue(child.Id);
            }
        return result.Order().ToList();
    }

    private static async Task<Dictionary<Guid, DeviceInfo>> LoadDevicesAsync(VedettaVipDbContext db, CancellationToken ct) =>
        await db.Devices.AsNoTracking()
            .Select(d => new DeviceInfo(d.Id, d.Name, d.Address, d.ParentDeviceId, d.CustomerId,
                d.Status != null ? d.Status.State : NodeState.Unknown))
            .ToDictionaryAsync(d => d.Id, ct);

    internal static MaintenanceSchedule.Window ToSchedule(MaintenanceWindow w) =>
        new(w.Recurrence, w.StartsAt, w.EndsAt, w.DaysOfWeek, w.StartTime, w.DurationMinutes, w.Enabled);
}
