// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using VedettaVip.Api.Data;
using VedettaVip.Api.Services;

namespace VedettaVip.Api.Endpoints;

/// <summary>
/// "Apri con WinBox": il browser apre il link winbox://&lt;indirizzo&gt;, che un gestore installato sul PC dell'operatore
/// passa a WinBox 4. Qui si scarica l'installer del gestore, con il percorso di WinBox delle Impostazioni già scritto dentro.
/// Nessuna credenziale: WinBox chiede utente e password (o usa i suoi indirizzi salvati).
/// </summary>
public static class WinBoxEndpoints
{
    public static IEndpointRouteBuilder MapWinBoxEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/winbox").WithTags("WinBox")
            .MapGet("/handler/{os}", DownloadHandlerAsync);
        return app;
    }

    /// <summary>Installer per "windows" o "linux" (404 per gli altri sistemi).</summary>
    private static async Task<Results<FileContentHttpResult, NotFound>> DownloadHandlerAsync(
        string os, VedettaVipDbContext db, CancellationToken ct)
    {
        var settings = await SettingsEndpoints.LoadWinBoxAsync(db, ct);
        var path = os.Equals("windows", StringComparison.OrdinalIgnoreCase) ? settings.WindowsPath : settings.LinuxPath;
        if (WinBoxHandlerScripts.Render(os, path) is not { } script)
            return TypedResults.NotFound();
        return TypedResults.File(Encoding.UTF8.GetBytes(script.Text), script.ContentType, script.FileName);
    }
}
