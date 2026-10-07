// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using VedettaVip.Shared.Contracts;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Components;

/// <summary>Gestione dei profili RouterOS (Admin): password solo in scrittura, vuota in modifica = invariata.</summary>
public partial class RouterOsCredentialsCard
{
    private const int ApiPort = 8728, ApiSslPort = 8729;
    private const string ApiService = "api", ApiSslService = "api-ssl";

    [Inject] private VedettaVipApiClient Api { get; set; } = default!;

    private IReadOnlyList<RouterOsCredentialDto>? profiles;
    private Guid? editingId;
    private string name = "";
    private string? description;
    private string username = "";
    private string password = "";
    /// <summary>Servizio RouterOS: testo e non bool, perché il select di Blazor non aggancia in modo affidabile un bool.</summary>
    private string service = ApiService;
    private bool useTls => service == ApiSslService;
    private int port = ApiPort;
    private bool verifyCertificate;
    private bool busy;
    private string? error;

    protected override Task OnInitializedAsync() => RunAsync(() => Task.CompletedTask);

    private bool CanSave() =>
        !busy && !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(username) && port is >= 1 and <= 65535
        && (editingId is not null || password.Length > 0);

    /// <summary>Cambiando servizio la porta segue il default, se non era stata personalizzata.</summary>
    private void OnServiceChanged()
    {
        if (port is ApiPort or ApiSslPort)
            port = useTls ? ApiSslPort : ApiPort;
    }

    private void Edit(RouterOsCredentialDto p)
    {
        (editingId, name, description, username, password) = (p.Id, p.Name, p.Description, p.Username, "");
        (service, port, verifyCertificate, error) = (p.UseTls ? ApiSslService : ApiService, p.Port, p.VerifyCertificate, null);
    }

    private void ResetForm()
    {
        (editingId, name, description, username, password) = (null, "", null, "", "");
        (service, port, verifyCertificate) = (ApiService, ApiPort, false);
    }

    private Task SaveAsync() => RunAsync(async () =>
    {
        var dto = new RouterOsCredentialUpsertDto(name.Trim(), username.Trim(), password.Length > 0 ? password : null,
            useTls, port, useTls && verifyCertificate, description);
        if (editingId is { } id)
            await Api.UpdateRouterOsCredentialAsync(id, dto, CancellationToken.None);
        else
            await Api.CreateRouterOsCredentialAsync(dto, CancellationToken.None);
        ResetForm();
    });

    private Task SetDefaultAsync(Guid? id) => RunAsync(() => Api.SetDefaultRouterOsCredentialAsync(id, CancellationToken.None));

    private Task DeleteAsync(RouterOsCredentialDto p) => RunAsync(async () =>
    {
        await Api.DeleteRouterOsCredentialAsync(p.Id, CancellationToken.None);
        if (editingId == p.Id) ResetForm();
    });

    private static string ServiceText(RouterOsCredentialDto p) =>
        (p.UseTls ? "api-ssl" : "api") + $" :{p.Port}" + (p.UseTls ? p.VerifyCertificate ? ", certificato verificato" : ", certificato non verificato" : "");

    private static string UsageText(RouterOsCredentialDto p)
    {
        var parts = new List<string>();
        if (p.DeviceCount > 0) parts.Add($"{p.DeviceCount} dispositivi");
        if (p.CustomerCount > 0) parts.Add($"{p.CustomerCount} clienti");
        if (p.IsDefault) parts.Add("predefinito");
        return parts.Count == 0 ? "non usato" : string.Join(", ", parts);
    }

    private async Task RunAsync(Func<Task> action)
    {
        busy = true;
        error = null;
        try
        {
            await action();
            profiles = await Api.GetRouterOsCredentialsAsync(CancellationToken.None);
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
