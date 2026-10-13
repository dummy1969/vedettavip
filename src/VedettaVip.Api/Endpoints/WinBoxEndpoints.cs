// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using VedettaVip.Api.Data;
using VedettaVip.Api.Services;

namespace VedettaVip.Api.Endpoints;

/// <summary>
/// "Apri con WinBox" e "Apri con PuTTY": il browser apre il link (winbox://, vedettavip-ssh://, vedettavip-telnet://), che un
/// gestore installato sul PC dell'operatore passa al programma. Qui si scarica l'installer del gestore, con i percorsi di
/// WinBox e PuTTY delle Impostazioni già scritti dentro. Nessuna credenziale: i programmi le chiedono.
/// La route resta /api/winbox/handler per i link già distribuiti.
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
        var settings = await SettingsEndpoints.LoadRemoteAccessAsync(db, ct);
        var windows = os.Equals("windows", StringComparison.OrdinalIgnoreCase);
        if (LinkHandlerScripts.Render(os, windows ? settings.WinBoxWindowsPath : settings.WinBoxLinuxPath,
                windows ? settings.PuttyWindowsPath : settings.PuttyLinuxPath) is not { } script)
            return TypedResults.NotFound();
        return TypedResults.File(Encoding.UTF8.GetBytes(script.Text), script.ContentType, script.FileName);
    }
}
