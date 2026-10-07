// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ApexCharts;
using Microsoft.AspNetCore.Components;
using VedettaVip.Web.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// URL dell'API da wwwroot/appsettings*.json (Api:BaseUrl); se assente si usa la stessa origine
// del sito (scenario reverse proxy in produzione). La barra finale serve per gli URL relativi.
var apiBaseUrl = builder.Configuration["Api:BaseUrl"];
var apiBase = new Uri(string.IsNullOrWhiteSpace(apiBaseUrl)
    ? builder.HostEnvironment.BaseAddress
    : apiBaseUrl.TrimEnd('/') + "/");

builder.Services.AddApexCharts();
// Cookie di sessione e header anti-CSRF su ogni chiamata; su 401 si va alla pagina di accesso
builder.Services.AddScoped(sp => new VedettaVipApiClient(
    new HttpClient(new ApiCredentialsHandler(sp.GetRequiredService<NavigationManager>())) { BaseAddress = apiBase }));
builder.Services.AddScoped<CurrentUser>();

await builder.Build().RunAsync();
