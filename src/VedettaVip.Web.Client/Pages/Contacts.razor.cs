// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using VedettaVip.Shared.Contracts;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Pages;

/// <summary>Contatti (destinatari) e loro iscrizioni: ambito tutto/cliente/mappa, filtro Down/Partial, ripristino.</summary>
public partial class Contacts : IDisposable
{
    [Inject] private VedettaVipApiClient Api { get; set; } = default!;

    /// <summary>Stato modificabile del form (i DTO sono record immutabili).</summary>
    private sealed class ContactForm
    {
        public string Name { get; set; } = "";
        public string? Email { get; set; }
        public string? TelegramChatId { get; set; }
        public bool Enabled { get; set; } = true;
        public List<SubscriptionForm> Subscriptions { get; } = [];
    }

    private sealed class SubscriptionForm
    {
        public SubscriptionScope Scope { get; set; } = SubscriptionScope.All;
        /// <summary>Id del cliente o della mappa come stringa (binding della select).</summary>
        public string TargetId { get; set; } = "";
        public AlertFilter Filter { get; set; } = AlertFilter.DownOnly;
        public bool NotifyRecovery { get; set; } = true;
    }

    private readonly CancellationTokenSource cts = new();
    private IReadOnlyList<ContactDto>? contacts;
    private IReadOnlyList<CustomerDto>? customers;
    private IReadOnlyList<MapSummaryDto>? maps;
    private ContactDto? editing;
    private ContactForm? form;
    private bool busy;
    private string? error;

    protected override Task OnInitializedAsync() => RunAsync(async () =>
    {
        customers = await Api.GetCustomersAsync(cts.Token);
        maps = await Api.GetMapsAsync(cts.Token);
    });

    private void New()
    {
        editing = null;
        form = new ContactForm();
        form.Subscriptions.Add(new SubscriptionForm());
    }

    private void Edit(ContactDto c)
    {
        editing = c;
        form = new ContactForm { Name = c.Name, Email = c.Email, TelegramChatId = c.TelegramChatId, Enabled = c.Enabled };
        foreach (var s in c.Subscriptions)
            form.Subscriptions.Add(new SubscriptionForm
            {
                Scope = s.Scope,
                TargetId = (s.CustomerId ?? s.MapId)?.ToString() ?? "",
                Filter = s.Filter,
                NotifyRecovery = s.NotifyRecovery
            });
    }

    private void Close() => (editing, form) = (null, null);

    private void AddSubscription() => form?.Subscriptions.Add(new SubscriptionForm());

    private Task SaveAsync() => RunAsync(async () =>
    {
        if (form is null) return;
        var subscriptions = form.Subscriptions.Select(s =>
        {
            Guid? target = Guid.TryParse(s.TargetId, out var id) ? id : null;
            return new SubscriptionUpsertDto(s.Scope,
                s.Scope == SubscriptionScope.Customer ? target : null,
                s.Scope == SubscriptionScope.Map ? target : null,
                s.Filter, s.NotifyRecovery);
        }).ToList();

        var dto = new ContactUpsertDto(form.Name.Trim(), Blank(form.Email), Blank(form.TelegramChatId), form.Enabled, subscriptions);
        if (editing is not null)
            await Api.UpdateContactAsync(editing.Id, dto, cts.Token);
        else
            await Api.CreateContactAsync(dto, cts.Token);
        Close();
    });

    private Task DeleteAsync(ContactDto c) => RunAsync(async () =>
    {
        await Api.DeleteContactAsync(c.Id, cts.Token);
        if (editing?.Id == c.Id) Close();
    });

    private async Task RunAsync(Func<Task> action)
    {
        busy = true;
        error = null;
        try
        {
            await action();
            contacts = await Api.GetContactsAsync(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (HttpRequestException ex)
        {
            error = ex.Message; // validazione: canali, chat ID, iscrizioni incomplete
        }
        finally
        {
            busy = false;
        }
    }

    private static string SubscriptionText(SubscriptionDto s)
    {
        var scope = s.Scope switch
        {
            SubscriptionScope.All => "Tutto",
            SubscriptionScope.Customer => $"Cliente {s.ScopeName}",
            _ => $"Mappa {s.ScopeName}"
        };
        var what = s.Filter switch
        {
            AlertFilter.DownOnly => "solo Down",
            AlertFilter.DownAndPartial => "Down e Partial",
            _ => "Down, Partial e soglie"
        };
        return $"{scope}: {what}{(s.NotifyRecovery ? ", ripristino" : "")}";
    }

    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    public void Dispose()
    {
        cts.Cancel();
        cts.Dispose();
    }
}
