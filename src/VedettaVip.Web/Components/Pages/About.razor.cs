// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

namespace VedettaVip.Web.Components.Pages;

public partial class About
{
    [Inject] private IOptions<AppOptions> App { get; set; } = default!;

    private string SourceUrl => App.Value.EffectiveSourceUrl;
    private static string Version => AppOptions.Version;

    /// <summary>Autori e contributori (in ordine di ingresso nel progetto).</summary>
    private static readonly string[] Authors = ["Marcello Anderlini — ideazione e sviluppo"];

    /// <summary>Componente di terze parti mostrato nei crediti (elenco completo in THIRD-PARTY-NOTICES.md).</summary>
    private sealed record Credit(string Name, string Version, string License, string Url, string Use);

    private static readonly Credit[] Credits =
    [
        new(".NET, ASP.NET Core, Blazor, EF Core, SignalR", "10.0", "MIT", "https://dotnet.microsoft.com/", "runtime, API, interfaccia web, accesso ai dati"),
        new("Npgsql / Npgsql.EntityFrameworkCore.PostgreSQL", "10.0.3", "PostgreSQL License", "https://www.npgsql.org/", "driver PostgreSQL"),
        new("Lextm.SharpSnmpLib", "12.5.7", "MIT", "https://github.com/lextudio/sharpsnmplib", "SNMP"),
        new("MailKit / MimeKit", "4.18.1", "MIT", "https://github.com/jstedfast/MailKit", "notifiche email"),
        new("BouncyCastle.Cryptography", "2.7.0", "MIT", "https://www.bouncycastle.org/", "crittografia (dipendenza di MimeKit)"),
        new("Blazor-ApexCharts", "6.0.2", "MIT", "https://github.com/apexcharts/Blazor-ApexCharts", "grafici"),
        new("ApexCharts.js", "4.7.0", "MIT", "https://github.com/apexcharts/apexcharts.js", "grafici (incluso in Blazor-ApexCharts)"),
        new("Bootstrap", "5.3.3", "MIT", "https://getbootstrap.com/", "stili dell'interfaccia"),
        new("Tabler Icons", "3.49.0", "MIT", "https://tabler.io/icons", "icone della mappa"),
        new("IEEE Registration Authority (MA-L, MA-M, MA-S)", "", "dati pubblici IEEE", "https://standards-oui.ieee.org/", "produttore dal MAC address"),
        new("PostgreSQL", "17", "PostgreSQL License", "https://www.postgresql.org/", "database"),
        new("TimescaleDB", "2.30.2", "Apache-2.0 / Timescale License", "https://github.com/timescale/timescaledb", "metriche (immagine Docker)"),
        new("Caddy", "2.11.6", "Apache-2.0", "https://caddyserver.com/", "reverse proxy HTTPS (immagine Docker)"),
    ];
}
