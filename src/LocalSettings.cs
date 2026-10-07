// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

// Sorgente condiviso da VedettaVip.Api, VedettaVip.Worker e VedettaVip.Web (Compile Include con Link nei csproj).
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Hosting;

namespace VedettaVip;

/// <summary>
/// Configurazione della singola installazione, fuori da Git: <c>appsettings.Local.json</c> e
/// <c>appsettings.{Environment}.Local.json</c> nella cartella dell'applicazione (facoltativi, ricaricati se cambiano).
/// Vengono subito dopo gli appsettings del repository: user-secrets, variabili d'ambiente e riga di comando restano
/// più forti.
/// </summary>
internal static class LocalSettings
{
    public static void AddLocalSettings(this IConfigurationManager configuration, IHostEnvironment environment)
    {
        var sources = ((IConfigurationBuilder)configuration).Sources;
        var index = -1;
        JsonConfigurationSource? appsettings = null;
        for (var i = 0; i < sources.Count; i++)
        {
            if (sources[i] is JsonConfigurationSource { Path: { } path } json &&
                path.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                appsettings = json;
            }
        }

        string[] files = ["appsettings.Local.json", $"appsettings.{environment.EnvironmentName}.Local.json"];
        foreach (var file in files)
        {
            sources.Insert(++index, new JsonConfigurationSource
            {
                Path = file,
                Optional = true,
                ReloadOnChange = true,
                FileProvider = appsettings?.FileProvider // stessa cartella degli appsettings (content root)
            });
        }
    }
}
