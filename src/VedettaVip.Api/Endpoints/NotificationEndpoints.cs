// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using VedettaVip.Api.Data;
using VedettaVip.Api.Security;
using VedettaVip.Api.Data.Entities;
using VedettaVip.Api.Services;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Endpoints;

/// <summary>Impostazioni dei canali, clienti, contatti con iscrizioni ed eventi.</summary>
public static partial class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var settings = app.MapGroup("/api/settings/notifications").WithTags("Settings").RequireAdmin();
        settings.MapGet("/", GetSettingsAsync);
        settings.MapPut("/", UpdateSettingsAsync);
        settings.MapPost("/test-email", TestEmailAsync);
        settings.MapPost("/test-telegram", TestTelegramAsync);

        var customers = app.MapGroup("/api/customers").WithTags("Customers");
        customers.MapGet("/", GetCustomersAsync);
        customers.MapPost("/", CreateCustomerAsync).RequireAdmin();
        customers.MapPut("/{id:guid}", UpdateCustomerAsync).RequireAdmin();
        customers.MapDelete("/{id:guid}", DeleteCustomerAsync).RequireAdmin();

        var contacts = app.MapGroup("/api/contacts").WithTags("Contacts").RequireAdmin();
        contacts.MapGet("/", GetContactsAsync);
        contacts.MapPost("/", CreateContactAsync);
        contacts.MapPut("/{id:guid}", UpdateContactAsync);
        contacts.MapDelete("/{id:guid}", DeleteContactAsync);

        var events = app.MapGroup("/api/events").WithTags("Events");
        events.MapGet("/", GetEventsAsync);
        events.MapPut("/{id:long}/ack", AcknowledgeAsync).RequireOperator();
        events.MapGet("/{id:long}/deliveries", GetDeliveriesAsync);

        return app;
    }

    // ---------- Impostazioni dei canali ----------

    private static async Task<Ok<NotificationSettingsDto>> GetSettingsAsync(
        VedettaVipDbContext db, NotificationSettingsService service, CancellationToken ct) =>
        TypedResults.Ok(NotificationSettingsService.ToDto(await service.LoadAsync(db, ct)));

    private static async Task<Results<Ok<NotificationSettingsDto>, ValidationProblem>> UpdateSettingsAsync(
        NotificationSettingsUpdateDto dto, VedettaVipDbContext db, NotificationSettingsService service, SecretProtector secrets, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (dto.EmailEnabled && string.IsNullOrWhiteSpace(dto.SmtpHost))
            errors[nameof(dto.SmtpHost)] = ["Indicare il server SMTP per attivare le email."];
        if (dto.EmailEnabled && string.IsNullOrWhiteSpace(dto.SmtpFromAddress))
            errors[nameof(dto.SmtpFromAddress)] = ["Indicare il mittente per attivare le email."];
        if (!IsKnownTimeZone(dto.TimeZoneId))
            errors[nameof(dto.TimeZoneId)] = [$"Fuso orario sconosciuto: {dto.TimeZoneId} (es. Europe/Rome)."];

        var s = await service.LoadAsync(db, ct);
        var hasTokenAfterSave = dto.TelegramBotToken switch
        {
            null => !string.IsNullOrEmpty(s.TelegramBotTokenProtected), // invariato
            "" => false,                                                // rimosso
            _ => true                                                   // nuovo
        };
        if (dto.TelegramEnabled && !hasTokenAfterSave)
            errors[nameof(dto.TelegramBotToken)] = ["Inserire il token del bot per attivare Telegram."];
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors);

        s.EmailEnabled = dto.EmailEnabled;
        s.SmtpHost = Blank(dto.SmtpHost);
        s.SmtpPort = dto.SmtpPort;
        s.SmtpSecurity = dto.SmtpSecurity;
        s.SmtpUsername = Blank(dto.SmtpUsername);
        s.SmtpFromAddress = Blank(dto.SmtpFromAddress);
        s.SmtpFromName = Blank(dto.SmtpFromName);
        s.SmtpValidateCertificate = dto.SmtpValidateCertificate;
        s.TelegramEnabled = dto.TelegramEnabled;
        s.DelaySeconds = dto.DelaySeconds;
        s.TimeZoneId = dto.TimeZoneId.Trim();
        s.PublicUrl = Blank(dto.PublicUrl)?.TrimEnd('/');
        s.ReminderMinutes = dto.ReminderMinutes;
        s.ReminderIncludeWarnings = dto.ReminderIncludeWarnings;

        // null = invariato, "" = rimuovi, altro = nuovo valore cifrato
        if (dto.SmtpPassword is not null)
            s.SmtpPasswordProtected = dto.SmtpPassword.Length == 0 ? null : secrets.Protect(dto.SmtpPassword);
        if (dto.TelegramBotToken is not null)
            s.TelegramBotTokenProtected = dto.TelegramBotToken.Length == 0 ? null : secrets.Protect(dto.TelegramBotToken.Trim());

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(NotificationSettingsService.ToDto(s));
    }

    /// <summary>Prova con le impostazioni salvate; l'errore del server SMTP torna in chiaro per la diagnosi.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> TestEmailAsync(
        TestEmailDto dto, VedettaVipDbContext db, NotificationSettingsService service, EmailSender email, TimeProvider time, CancellationToken ct)
    {
        var s = await service.LoadAsync(db, ct);
        var (config, problem) = service.Smtp(s);
        if (config is null)
            return DbProblems.Problem(StatusCodes.Status400BadRequest, "Email non configurata", problem);

        try
        {
            await email.SendAsync(config, dto.To, "[VedettaVip] Email di prova",
                $"Email di prova inviata da VedettaVip alle {TimeZoneInfo.ConvertTime(time.GetUtcNow(), service.TimeZone(s)):dd/MM/yyyy HH:mm:ss}.\n" +
                $"Server {config.Host}:{config.Port}, sicurezza {config.Security}.", ct);
            return TypedResults.NoContent();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return DbProblems.Problem(StatusCodes.Status502BadGateway, "Invio email non riuscito", ex.Message);
        }
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> TestTelegramAsync(
        TestTelegramDto dto, VedettaVipDbContext db, NotificationSettingsService service, TelegramSender telegram, CancellationToken ct)
    {
        var (token, problem) = service.Telegram(await service.LoadAsync(db, ct));
        if (token is null)
            return DbProblems.Problem(StatusCodes.Status400BadRequest, "Telegram non configurato", problem);
        if (!IsTelegramChatId(dto.ChatId))
            return DbProblems.Problem(StatusCodes.Status400BadRequest, "Chat ID non valido", "Atteso un numero (negativo per i gruppi) o @nomecanale.");

        try
        {
            await telegram.SendAsync(token, dto.ChatId.Trim(), "VedettaVip: messaggio di prova.", ct);
            return TypedResults.NoContent();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return DbProblems.Problem(StatusCodes.Status502BadGateway, "Invio Telegram non riuscito", ex.Message);
        }
    }

    // ---------- Clienti ----------

    private static async Task<Ok<List<CustomerDto>>> GetCustomersAsync(VedettaVipDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.Customers.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CustomerDto(c.Id, c.Name, c.Notes, db.Devices.Count(d => d.CustomerId == c.Id), c.SnmpCredentialId,
                c.RouterOsCredentialId))
            .ToListAsync(ct));

    private static async Task<Results<Created<CustomerDto>, ValidationProblem, ProblemHttpResult>> CreateCustomerAsync(
        CustomerUpsertDto dto, VedettaVipDbContext db, AgentNotifier agents, CancellationToken ct)
    {
        if (await CheckSnmpCredentialAsync(db, dto, ct) is { } invalid)
            return invalid;
        var customer = new Customer
        {
            Id = Guid.CreateVersion7(), Name = dto.Name.Trim(), Notes = Blank(dto.Notes), SnmpCredentialId = dto.SnmpCredentialId,
            RouterOsCredentialId = dto.RouterOsCredentialId
        };
        db.Customers.Add(customer);
        if (await db.TrySaveChangesAsync(ct) is { } problem)
            return problem; // nome duplicato → 409
        return TypedResults.Created($"/api/customers/{customer.Id}",
            new CustomerDto(customer.Id, customer.Name, customer.Notes, 0, customer.SnmpCredentialId, customer.RouterOsCredentialId));
    }

    /// <summary>Se cambia il profilo SNMP del cliente, gli agenti rileggono i target (community dei suoi device).</summary>
    private static async Task<Results<NoContent, NotFound, ValidationProblem, ProblemHttpResult>> UpdateCustomerAsync(
        Guid id, CustomerUpsertDto dto, VedettaVipDbContext db, AgentNotifier agents, CancellationToken ct)
    {
        if (await CheckSnmpCredentialAsync(db, dto, ct) is { } invalid)
            return invalid;
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null)
            return TypedResults.NotFound();
        var snmpChanged = customer.SnmpCredentialId != dto.SnmpCredentialId || customer.RouterOsCredentialId != dto.RouterOsCredentialId;
        customer.Name = dto.Name.Trim();
        customer.Notes = Blank(dto.Notes);
        customer.SnmpCredentialId = dto.SnmpCredentialId;
        customer.RouterOsCredentialId = dto.RouterOsCredentialId;
        if (await db.TrySaveChangesAsync(ct) is { } problem)
            return problem;
        if (snmpChanged)
            await agents.TargetsChangedAsync();
        return TypedResults.NoContent();
    }

    private static async Task<ValidationProblem?> CheckSnmpCredentialAsync(VedettaVipDbContext db, CustomerUpsertDto dto, CancellationToken ct)
    {
        if (dto.SnmpCredentialId is { } cid && !await db.SnmpCredentials.AnyAsync(c => c.Id == cid, ct))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(dto.SnmpCredentialId)] = ["Il profilo SNMP non esiste."] });
        if (dto.RouterOsCredentialId is { } rid && !await db.RouterOsCredentials.AnyAsync(c => c.Id == rid, ct))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(dto.RouterOsCredentialId)] = ["Il profilo RouterOS non esiste."] });
        return null;
    }

    /// <summary>I device del cliente restano senza cliente (SET NULL); le iscrizioni al cliente vengono eliminate.</summary>
    private static async Task<Results<NoContent, NotFound>> DeleteCustomerAsync(Guid id, VedettaVipDbContext db, CancellationToken ct)
    {
        var deleted = await db.Customers.Where(c => c.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    // ---------- Contatti e iscrizioni ----------

    private static async Task<Ok<List<ContactDto>>> GetContactsAsync(VedettaVipDbContext db, CancellationToken ct)
    {
        var contacts = await db.Contacts.AsNoTracking()
            .Include(c => c.Subscriptions).ThenInclude(s => s.Customer)
            .Include(c => c.Subscriptions).ThenInclude(s => s.Map)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);
        return TypedResults.Ok(contacts.Select(ToDto).ToList());
    }

    private static async Task<Results<Created<ContactDto>, ValidationProblem, ProblemHttpResult>> CreateContactAsync(
        ContactUpsertDto dto, VedettaVipDbContext db, CancellationToken ct)
    {
        if (await ValidateContactAsync(db, dto, ct) is { } errors)
            return TypedResults.ValidationProblem(errors);

        var contact = new Contact { Id = Guid.CreateVersion7(), Name = "" };
        Apply(contact, dto);
        db.Contacts.Add(contact);
        if (await db.TrySaveChangesAsync(ct) is { } problem)
            return problem;

        return TypedResults.Created($"/api/contacts/{contact.Id}", await LoadContactDtoAsync(db, contact.Id, ct));
    }

    private static async Task<Results<Ok<ContactDto>, NotFound, ValidationProblem, ProblemHttpResult>> UpdateContactAsync(
        Guid id, ContactUpsertDto dto, VedettaVipDbContext db, CancellationToken ct)
    {
        var contact = await db.Contacts.Include(c => c.Subscriptions).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contact is null)
            return TypedResults.NotFound();
        if (await ValidateContactAsync(db, dto, ct) is { } errors)
            return TypedResults.ValidationProblem(errors);

        // Le iscrizioni sono sostituite per intero
        db.Subscriptions.RemoveRange(contact.Subscriptions);
        contact.Subscriptions = [];
        Apply(contact, dto);
        // Add esplicito: hanno già l'Id, scoperte dalla navigazione di un contatto tracciato EF le aggiornerebbe (UPDATE su 0 righe)
        db.Subscriptions.AddRange(contact.Subscriptions);
        if (await db.TrySaveChangesAsync(ct) is { } problem)
            return problem;

        return TypedResults.Ok(await LoadContactDtoAsync(db, id, ct));
    }

    private static async Task<Results<NoContent, NotFound>> DeleteContactAsync(Guid id, VedettaVipDbContext db, CancellationToken ct)
    {
        var deleted = await db.Contacts.Where(c => c.Id == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static void Apply(Contact contact, ContactUpsertDto dto)
    {
        contact.Name = dto.Name.Trim();
        contact.Email = Blank(dto.Email);
        contact.TelegramChatId = Blank(dto.TelegramChatId);
        contact.Enabled = dto.Enabled;
        foreach (var s in dto.Subscriptions ?? [])
            contact.Subscriptions.Add(new Subscription
            {
                Id = Guid.CreateVersion7(),
                Scope = s.Scope,
                CustomerId = s.Scope == SubscriptionScope.Customer ? s.CustomerId : null,
                MapId = s.Scope == SubscriptionScope.Map ? s.MapId : null,
                Filter = s.Filter,
                NotifyRecovery = s.NotifyRecovery
            });
    }

    private static async Task<Dictionary<string, string[]>?> ValidateContactAsync(VedettaVipDbContext db, ContactUpsertDto dto, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(dto.Email) && string.IsNullOrWhiteSpace(dto.TelegramChatId))
            errors["Channels"] = ["Indicare almeno un'email o un chat ID Telegram."];
        if (!string.IsNullOrWhiteSpace(dto.TelegramChatId) && !IsTelegramChatId(dto.TelegramChatId))
            errors[nameof(dto.TelegramChatId)] = ["Chat ID non valido: un numero (negativo per i gruppi) o @nomecanale."];

        var subs = dto.Subscriptions ?? [];
        var customerIds = subs.Where(s => s.Scope == SubscriptionScope.Customer).Select(s => s.CustomerId).ToList();
        var mapIds = subs.Where(s => s.Scope == SubscriptionScope.Map).Select(s => s.MapId).ToList();
        if (customerIds.Any(id => id is null) || mapIds.Any(id => id is null))
            errors[nameof(dto.Subscriptions)] = ["Ogni iscrizione a un cliente o a una mappa deve indicare quale."];
        else
        {
            var ids = customerIds.Select(i => i!.Value).Distinct().ToList();
            if (await db.Customers.CountAsync(c => ids.Contains(c.Id), ct) != ids.Count)
                errors[nameof(dto.Subscriptions)] = ["Cliente inesistente in un'iscrizione."];
            var maps = mapIds.Select(i => i!.Value).Distinct().ToList();
            if (await db.Maps.CountAsync(m => maps.Contains(m.Id), ct) != maps.Count)
                errors[nameof(dto.Subscriptions)] = ["Mappa inesistente in un'iscrizione."];
        }

        return errors.Count > 0 ? errors : null;
    }

    private static async Task<ContactDto> LoadContactDtoAsync(VedettaVipDbContext db, Guid id, CancellationToken ct) =>
        ToDto(await db.Contacts.AsNoTracking()
            .Include(c => c.Subscriptions).ThenInclude(s => s.Customer)
            .Include(c => c.Subscriptions).ThenInclude(s => s.Map)
            .FirstAsync(c => c.Id == id, ct));

    private static ContactDto ToDto(Contact c) => new(c.Id, c.Name, c.Email, c.TelegramChatId, c.Enabled,
        c.Subscriptions
            .OrderBy(s => s.Scope).ThenBy(s => s.Customer?.Name ?? s.Map?.Name)
            .Select(s => new SubscriptionDto(s.Id, s.Scope, s.CustomerId, s.MapId, s.Customer?.Name ?? s.Map?.Name, s.Filter, s.NotifyRecovery))
            .ToList());

    // ---------- Eventi ----------

    /// <summary>
    /// Eventi più recenti (default 200, massimo 1000), filtrabili per device, per "non presi in carico" e per periodo
    /// (<c>since</c> incluso, <c>before</c> escluso).
    /// </summary>
    private static async Task<Ok<List<EventDto>>> GetEventsAsync(
        VedettaVipDbContext db, CancellationToken ct, Guid? deviceId = null, bool unacknowledgedOnly = false,
        DateTimeOffset? before = null, DateTimeOffset? since = null, int limit = 200)
    {
        var query = db.Events.AsNoTracking();
        if (deviceId is { } id) query = query.Where(e => e.DeviceId == id);
        if (unacknowledgedOnly) query = query.Where(e => !e.Acknowledged);
        if (before is { } b) query = query.Where(e => e.Time < b.ToUniversalTime());
        if (since is { } s) query = query.Where(e => e.Time >= s.ToUniversalTime());

        var events = await query
            .OrderByDescending(e => e.Time).ThenByDescending(e => e.Id)
            .Take(Math.Clamp(limit, 1, 1000))
            .Select(e => new EventDto(
                e.Id, e.Time, e.Severity, e.Type, e.Message, e.DeviceId,
                e.Device != null ? e.Device.Name : null, e.AgentId,
                e.Device != null && e.Device.Customer != null ? e.Device.Customer.Name : null,
                e.FromState, e.ToState, e.Acknowledged, e.ResolvedAt, e.AcknowledgedBy, e.AcknowledgedAt, e.NotifyState, e.NotifyNote,
                e.Deliveries.Count(d => d.Status == DeliveryStatus.Sent),
                e.Deliveries.Count(d => d.Status == DeliveryStatus.Pending),
                e.Deliveries.Count(d => d.Status == DeliveryStatus.Failed)))
            .ToListAsync(ct);

        return TypedResults.Ok(events);
    }

    /// <summary>Presa in carico (con nome dell'utente e ora) o riapertura (azzera anche la chiusura automatica).</summary>
    private static async Task<Results<NoContent, NotFound>> AcknowledgeAsync(
        long id, EventAckDto dto, ClaimsPrincipal principal, UserManager<AppUser> users, VedettaVipDbContext db, TimeProvider time, CancellationToken ct)
    {
        var by = dto.Acknowledged ? (await users.GetUserAsync(principal))?.DisplayName ?? principal.Identity?.Name : null;
        DateTimeOffset? at = dto.Acknowledged ? time.GetUtcNow() : null;
        var updated = await db.Events.Where(e => e.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Acknowledged, dto.Acknowledged)
                .SetProperty(e => e.ResolvedAt, e => dto.Acknowledged ? e.ResolvedAt : null)
                .SetProperty(e => e.AcknowledgedBy, by)
                .SetProperty(e => e.AcknowledgedAt, at), ct);
        return updated == 0 ? TypedResults.NotFound() : TypedResults.NoContent();
    }

    private static async Task<Ok<List<DeliveryDto>>> GetDeliveriesAsync(long id, VedettaVipDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.NotificationDeliveries.AsNoTracking()
            .Where(d => d.EventId == id)
            .OrderBy(d => d.Id)
            .Select(d => new DeliveryDto(d.Id, d.Channel, d.ContactName, d.Destination, d.Status, d.Attempts, d.LastError, d.CreatedAt, d.SentAt))
            .ToListAsync(ct));

    // ---------- Utilità ----------

    [GeneratedRegex(@"^(-?\d{1,20}|@[A-Za-z][A-Za-z0-9_]{4,31})$")]
    private static partial Regex TelegramChatIdRegex();

    internal static bool IsTelegramChatId(string value) => TelegramChatIdRegex().IsMatch(value.Trim());

    private static bool IsKnownTimeZone(string id)
    {
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
