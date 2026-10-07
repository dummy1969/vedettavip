// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using VedettaVip.Shared.Contracts;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Components;

/// <summary>Gestione dei profili SNMP (Admin): la community è solo in scrittura, vuota in modifica = invariata.</summary>
public partial class SnmpCredentialsCard
{
    [Inject] private VedettaVipApiClient Api { get; set; } = default!;

    private IReadOnlyList<SnmpCredentialDto>? profiles;
    private Guid? editingId;
    private string name = "";
    private string? description;
    private string community = "";
    private bool busy;
    private string? error;

    protected override Task OnInitializedAsync() => RunAsync(() => Task.CompletedTask);

    private bool CanSave() => !busy && !string.IsNullOrWhiteSpace(name) && (editingId is not null || community.Length > 0);

    private void Edit(SnmpCredentialDto p) => (editingId, name, description, community, error) = (p.Id, p.Name, p.Description, "", null);

    private void ResetForm() => (editingId, name, description, community) = (null, "", null, "");

    private Task SaveAsync() => RunAsync(async () =>
    {
        var dto = new SnmpCredentialUpsertDto(name.Trim(), description, community.Length > 0 ? community : null);
        if (editingId is { } id)
            await Api.UpdateSnmpCredentialAsync(id, dto, CancellationToken.None);
        else
            await Api.CreateSnmpCredentialAsync(dto, CancellationToken.None);
        ResetForm();
    });

    private Task SetDefaultAsync(Guid? id) => RunAsync(() => Api.SetDefaultSnmpCredentialAsync(id, CancellationToken.None));

    private Task DeleteAsync(SnmpCredentialDto p) => RunAsync(async () =>
    {
        await Api.DeleteSnmpCredentialAsync(p.Id, CancellationToken.None);
        if (editingId == p.Id) ResetForm();
    });

    private static string UsageText(SnmpCredentialDto p)
    {
        var parts = new List<string>();
        if (p.DeviceCount > 0) parts.Add($"{p.DeviceCount} dispositivi");
        if (p.CustomerCount > 0) parts.Add($"{p.CustomerCount} clienti");
        if (p.IsDefault) parts.Add("predefinito");
        return parts.Count == 0 ? "non usato" : string.Join(", ", parts);
    }

    /// <summary>Esegue l'azione e ricarica l'elenco; l'errore (es. 409 profilo in uso) resta visibile.</summary>
    private async Task RunAsync(Func<Task> action)
    {
        busy = true;
        error = null;
        try
        {
            await action();
            profiles = await Api.GetSnmpCredentialsAsync(CancellationToken.None);
        }
        catch (HttpRequestException ex)
        {
            error = ex.Message;
            profiles ??= [];
        }
        finally
        {
            busy = false;
        }
    }
}
