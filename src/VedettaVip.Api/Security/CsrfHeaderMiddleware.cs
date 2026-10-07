// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

namespace VedettaVip.Api.Security;

/// <summary>
/// Difesa CSRF per le API autenticate con cookie: le richieste che modificano dati devono avere l'header
/// X-VedettaVip-Request. Un sito terzo non può aggiungerlo senza una preflight CORS, consentita solo alle origini di
/// VedettaVip. Esclusi gli endpoint degli agenti (autenticati con X-Agent-Key, nessun cookie).
/// </summary>
public sealed class CsrfHeaderMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-VedettaVip-Request";

    public Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method)
            || !request.Path.StartsWithSegments("/api") || request.Path.StartsWithSegments("/api/agent")
            || request.Headers.ContainsKey(HeaderName))
            return next(context);

        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return context.Response.WriteAsJsonAsync(new
        {
            title = "Richiesta rifiutata",
            detail = $"Header {HeaderName} mancante (protezione CSRF).",
            status = 400
        });
    }
}
