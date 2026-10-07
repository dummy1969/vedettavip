// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.Extensions.Options;
using VedettaVip.Api.Options;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Security;

/// <summary>
/// Protegge l'hub degli agenti con la chiave X-Agent-Key (stesso controllo di <see cref="AgentKeyFilter"/>,
/// che vale solo per gli endpoint minimal API, non per gli hub). Copre sia la negotiate sia la connessione
/// WebSocket: il client SignalR .NET invia l'header su entrambe.
/// </summary>
public sealed class AgentKeyMiddleware(RequestDelegate next, IOptions<AgentOptions> options, ILogger<AgentKeyMiddleware> logger)
{
    private readonly byte[] expectedHash = ApiKeyComparer.Hash(options.Value.ApiKey);

    public Task InvokeAsync(HttpContext context)
    {
        if (ApiKeyComparer.Matches(context.Request.Headers[AgentHeaders.ApiKey], expectedHash))
            return next(context);

        // Mai loggare la chiave ricevuta
        logger.LogWarning("Connessione all'hub agent rifiutata da {RemoteIp}: chiave assente o errata",
            context.Connection.RemoteIpAddress);
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }
}
