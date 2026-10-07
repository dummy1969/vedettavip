// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.Extensions.Options;
using VedettaVip.Api.Options;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Security;

/// <summary>Protegge gli endpoint /api/agent con la chiave nell'header X-Agent-Key.</summary>
public sealed class AgentKeyFilter(IOptions<AgentOptions> options, ILogger<AgentKeyFilter> logger) : IEndpointFilter
{
    private readonly byte[] expectedHash = ApiKeyComparer.Hash(options.Value.ApiKey);

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (ApiKeyComparer.Matches(http.Request.Headers[AgentHeaders.ApiKey], expectedHash))
            return next(context);

        // Mai loggare la chiave ricevuta
        logger.LogWarning("Richiesta agent rifiutata da {RemoteIp} su {Path}: chiave assente o errata",
            http.Connection.RemoteIpAddress, http.Request.Path);
        return ValueTask.FromResult<object?>(TypedResults.Unauthorized());
    }
}
