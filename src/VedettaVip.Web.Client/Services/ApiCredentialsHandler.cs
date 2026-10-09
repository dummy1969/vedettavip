// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Web.Client.Services;

/// <summary>
/// Per ogni chiamata all'API: invia il cookie di sessione (credentials: include, l'API è su un'altra origine in
/// sviluppo), l'header anti-CSRF X-VedettaVip-Request e l'id della scheda (X-VedettaVip-Client, riportato nei messaggi
/// MapsChanged). Su 401 porta alla pagina di accesso, tornando poi alla pagina corrente.
/// </summary>
public sealed class ApiCredentialsHandler(NavigationManager navigation) : DelegatingHandler(new HttpClientHandler())
{
    public const string CsrfHeader = "X-VedettaVip-Request";

    /// <summary>Id casuale di questa scheda del browser (un'istanza WebAssembly per scheda).</summary>
    public static readonly string ClientId = Guid.NewGuid().ToString("N");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        request.Headers.TryAddWithoutValidation(CsrfHeader, "1");
        request.Headers.TryAddWithoutValidation(MapClientHeaders.ClientId, ClientId);

        var response = await base.SendAsync(request, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized
            && request.RequestUri?.AbsolutePath.StartsWith("/api/auth/", StringComparison.Ordinal) != true)
            RedirectToLogin(navigation);

        return response;
    }

    public static void RedirectToLogin(NavigationManager navigation)
    {
        var relative = navigation.ToBaseRelativePath(navigation.Uri);
        if (!relative.StartsWith("login", StringComparison.OrdinalIgnoreCase))
            navigation.NavigateTo($"login?returnUrl={Uri.EscapeDataString("/" + relative)}");
    }
}

/// <summary>Per il client SignalR: invia il cookie anche alla negotiate (la connessione WebSocket lo invia da sola).</summary>
public sealed class IncludeCredentialsHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        return base.SendAsync(request, ct);
    }
}
