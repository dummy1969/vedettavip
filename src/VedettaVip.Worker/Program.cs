// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.Extensions.Options;
using VedettaVip;
using VedettaVip.Shared.Contracts;
using VedettaVip.Worker.Api;
using VedettaVip.Worker.Options;
using VedettaVip.Worker.Probes;
using VedettaVip.Worker.Services;
using VedettaVip.Worker.State;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddLocalSettings(builder.Environment);

// Opzioni validate all'avvio: senza Agent:ApiKey o Agent:ApiBaseUrl il Worker non parte.
// Il Worker non accede al database: parla solo con l'API.
builder.Services.AddOptions<PollingOptions>()
    .BindConfiguration(PollingOptions.Section)
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<AgentOptions>()
    .BindConfiguration(AgentOptions.Section)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddHttpClient<AgentApiClient>((sp, http) =>
{
    var agent = sp.GetRequiredService<IOptions<AgentOptions>>().Value;
    http.BaseAddress = new Uri(agent.ApiBaseUrl.TrimEnd('/') + "/");
    http.Timeout = TimeSpan.FromSeconds(agent.HttpTimeoutSeconds);
    http.DefaultRequestHeaders.Add(AgentHeaders.ApiKey, agent.ApiKey);
    // Identifica l'agente che prende una scansione (le altre richieste dello stesso compito ricevono 409)
    http.DefaultRequestHeaders.Add("X-Agent-Id", agent.AgentId);
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IcmpProbe>();
builder.Services.AddSingleton<SnmpProbe>();
builder.Services.AddSingleton<DeviceStateStore>();
builder.Services.AddSingleton<TrafficCalculator>();

// Servizi singleton e insieme hosted: PollingService li usa direttamente
builder.Services.AddSingleton<TargetProvider>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TargetProvider>());
builder.Services.AddSingleton<StatusPublisher>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<StatusPublisher>());
builder.Services.AddHostedService<PollingService>();
builder.Services.AddSingleton<InterfaceInventoryService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<InterfaceInventoryService>());
builder.Services.AddHostedService<AgentHubListener>();
builder.Services.AddHostedService<RouterOsPollingService>();
builder.Services.AddSingleton<NeighborDiscoveryService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<NeighborDiscoveryService>());
builder.Services.AddSingleton<SubnetScanService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SubnetScanService>());

var host = builder.Build();
host.Run();
