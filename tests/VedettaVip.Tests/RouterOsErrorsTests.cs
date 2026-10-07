// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using VedettaVip.Worker.Probes;

namespace VedettaVip.Tests;

/// <summary>Errori RouterOS riprodotti con connessioni vere su localhost, poi tradotti da RouterOsErrors.</summary>
public class RouterOsErrorsTests
{
    /// <summary>Server che accetta e chiude subito (come un router con l'IP non ammesso in /ip service).</summary>
    private static (TcpListener Listener, Task Serving) ClosingServer()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var serving = Task.Run(async () => { using var c = await l.AcceptTcpClientAsync(); });
        return (l, serving);
    }

    private static async Task<Exception> ConnectError(int port, bool tls, bool verify = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return await Record.ExceptionAsync(() => RouterOsApiClient.ConnectAsync("127.0.0.1", port, tls, verify, "vedettavip", "x", timeout.Token))
               ?? throw new Exception("nessun errore");
    }

    [Fact]
    public async Task Closed_port()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        var ex = await ConnectError(port, tls: false);
        Assert.StartsWith($"porta {port} chiusa: servizio api disabilitato", RouterOsErrors.Describe(ex, false, port));
    }

    [Fact]
    public async Task Router_closing_the_plain_connection()
    {
        var (l, serving) = ClosingServer();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        var ex = await ConnectError(port, tls: false);
        await serving; l.Stop();
        Assert.StartsWith("connessione chiusa dal router: indirizzo non ammesso in /ip service api", RouterOsErrors.Describe(ex, false, port));
    }

    [Fact]
    public async Task Router_closing_during_tls()
    {
        var (l, serving) = ClosingServer();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        var ex = await ConnectError(port, tls: true);
        await serving; l.Stop();
        Assert.StartsWith("connessione chiusa durante il TLS", RouterOsErrors.Describe(ex, true, port));
    }

    [Fact]
    public async Task Self_signed_certificate_with_verification_on()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=router", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var ephemeral = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var cert = X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pfx), null);
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        var serving = Task.Run(async () =>
        {
            using var c = await l.AcceptTcpClientAsync();
            using var ssl = new SslStream(c.GetStream());
            try { await ssl.AuthenticateAsServerAsync(cert); } catch (Exception) { }
        });

        var ex = await ConnectError(port, tls: true, verify: true);
        await serving; l.Stop();
        Assert.StartsWith("certificato del router non attendibile", RouterOsErrors.Describe(ex, true, port));
    }

    [Fact]
    public void Api_ssl_without_certificate()
    {
        // Ciò che .NET restituisce quando il router offre solo cifrari anonimi (api-ssl senza certificato)
        var ex = new AuthenticationException("Authentication failed, see inner exception.",
            new Exception("SSL Handshake failed with OpenSSL error - SSL_ERROR_SSL.",
                new Exception("error:0A000410:SSL routines::sslv3 alert handshake failure")));
        Assert.StartsWith("TLS rifiutato dal router: api-ssl senza certificato?", RouterOsErrors.Describe(ex, true, 8729));
    }

    [Fact]
    public void Router_answer_is_kept() =>
        Assert.Equal("invalid user name or password (6)", RouterOsErrors.Describe(new RouterOsApiException("invalid user name or password (6)"), true, 8729));

    [Fact]
    public void Service_text() => Assert.Equal(("api :8728", "api-ssl :18729"), (RouterOsErrors.Service(false, 8728), RouterOsErrors.Service(true, 18729)));
}
