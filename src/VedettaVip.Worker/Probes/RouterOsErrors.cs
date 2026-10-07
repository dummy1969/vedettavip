// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Net.Sockets;
using System.Security.Authentication;

namespace VedettaVip.Worker.Probes;

/// <summary>
/// Traduce gli errori di connessione all'API RouterOS nella causa probabile e in cosa controllare sul router.
/// Le eccezioni di .NET ("Authentication failed, see inner exception") da sole non dicono nulla a chi gestisce il MikroTik.
/// </summary>
public static class RouterOsErrors
{
    public static string Service(bool useTls, int port) => $"{(useTls ? "api-ssl" : "api")} :{port}";

    public static string Describe(Exception ex, bool useTls, int port)
    {
        var service = Service(useTls, port);
        var detail = Innermost(ex).Message;
        var all = string.Join(" | ", Chain(ex).Select(e => e.Message));

        return ex switch
        {
            RouterOsApiException => ex.Message, // risposta del router, es. "invalid user name or password (6)"

            SocketException { SocketErrorCode: SocketError.ConnectionRefused } =>
                $"porta {port} chiusa: servizio {(useTls ? "api-ssl" : "api")} disabilitato o su un'altra porta (/ip service)",
            SocketException { SocketErrorCode: SocketError.TimedOut or SocketError.HostUnreachable or SocketError.NetworkUnreachable } =>
                $"router non raggiungibile su {service} ({detail})",
            SocketException s => $"{s.SocketErrorCode} su {service}",

            AuthenticationException when Contains(all, "handshake failure", "alert number 40", "no shared cipher", "sslv3 alert") =>
                "TLS rifiutato dal router: api-ssl senza certificato? Assegnarne uno: /ip service set api-ssl certificate=...",
            AuthenticationException when Contains(all, "certificate", "RemoteCertificate", "untrusted", "chain") =>
                "certificato del router non attendibile o con nome diverso: disattivare la verifica nel profilo (autofirmato)",
            // Chiusura durante la negoziazione: .NET la segnala come IOException ("unexpected EOF ... transport stream"),
            // a volte come AuthenticationException; EndOfStreamException invece arriva dalla lettura del protocollo
            AuthenticationException or IOException when useTls && ex is not EndOfStreamException
                                                     && Contains(all, "EOF", "0 bytes", "transport stream", "connection reset") =>
                $"connessione chiusa durante il TLS: indirizzo non ammesso in /ip service api-ssl, oppure la porta {port} è del servizio api (in chiaro)",
            AuthenticationException => $"TLS: {detail}",

            EndOfStreamException or IOException when !useTls =>
                $"connessione chiusa dal router: indirizzo non ammesso in /ip service api, oppure la porta {port} è del servizio api-ssl (scegliere api-ssl nel profilo)",
            EndOfStreamException or IOException =>
                $"connessione chiusa dal router su {service}: indirizzo non ammesso in /ip service o troppe sessioni API",

            _ => $"{detail} ({service})"
        };
    }

    private static bool Contains(string text, params string[] parts) =>
        parts.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<Exception> Chain(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            yield return e;
    }

    private static Exception Innermost(Exception ex) => Chain(ex).Last();
}
