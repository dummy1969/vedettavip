// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using VedettaVip.Shared.Contracts;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Pages;

/// <summary>Anagrafica clienti: eliminare un cliente lascia i suoi dispositivi senza cliente e cancella le iscrizioni.</summary>
public partial class Customers : IDisposable
{
    [Inject] private VedettaVipApiClient Api { get; set; } = default!;

    private readonly CancellationTokenSource cts = new();
    private IReadOnlyList<CustomerDto>? customers;
    private Guid? editingId;
    private string name = "";
    private string? notes;
    private string snmpCredentialId = "";
    private IReadOnlyList<SnmpCredentialDto> profiles = [];
    private string routerOsCredentialId = "";
    private IReadOnlyList<RouterOsCredentialDto> rosProfiles = [];
    private bool busy;
    private string? error;

    protected override Task OnInitializedAsync() => RunAsync(async () =>
    {
        profiles = await Api.GetSnmpCredentialsAsync(cts.Token);
        rosProfiles = await Api.GetRouterOsCredentialsAsync(cts.Token);
    });

    private void Edit(CustomerDto c) =>
        (editingId, name, notes, snmpCredentialId, routerOsCredentialId) =
        (c.Id, c.Name, c.Notes, c.SnmpCredentialId?.ToString() ?? "", c.RouterOsCredentialId?.ToString() ?? "");

    private void ResetForm() => (editingId, name, notes, snmpCredentialId, routerOsCredentialId) = (null, "", null, "", "");

    private Task SaveAsync() => RunAsync(async () =>
    {
        var dto = new CustomerUpsertDto(name.Trim(), notes, Guid.TryParse(snmpCredentialId, out var p) ? p : null,
            Guid.TryParse(routerOsCredentialId, out var r) ? r : null);
        if (editingId is { } id)
            await Api.UpdateCustomerAsync(id, dto, cts.Token);
        else
            await Api.CreateCustomerAsync(dto, cts.Token);
        ResetForm();
    });

    private Task DeleteAsync(CustomerDto c) => RunAsync(async () =>
    {
        await Api.DeleteCustomerAsync(c.Id, cts.Token);
        if (editingId == c.Id) ResetForm();
    });

    /// <summary>Esegue l'azione e ricarica l'elenco; gli errori (es. nome duplicato) restano visibili in pagina.</summary>
    private async Task RunAsync(Func<Task> action)
    {
        busy = true;
        error = null;
        try
        {
            await action();
            customers = await Api.GetCustomersAsync(cts.Token);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (HttpRequestException ex)
        {
            error = ex.Message;
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
