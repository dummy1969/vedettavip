// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip;
using VedettaVip.Web.Client.Pages;
using VedettaVip.Web.Components;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddLocalSettings(builder.Environment);

// Link al sorgente nel footer e nella pagina About (App:SourceUrl, obbligo AGPL per le versioni modificate)
builder.Services.Configure<VedettaVip.Web.AppOptions>(builder.Configuration.GetSection(VedettaVip.Web.AppOptions.Section));

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(VedettaVip.Web.Client._Imports).Assembly);

app.Run();
