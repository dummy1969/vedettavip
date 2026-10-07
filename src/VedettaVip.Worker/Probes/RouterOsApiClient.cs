// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace VedettaVip.Worker.Probes;

/// <summary>Errore restituito dal router (!trap / !fatal) o violazione del protocollo.</summary>
public sealed class RouterOsApiException(string message) : Exception(message);

/// <summary>
/// Client minimo del protocollo API di RouterOS (servizi "api", TCP 8728, e "api-ssl", TLS 8729), valido per v6 e v7.
/// Una frase è una sequenza di parole con prefisso di lunghezza variabile, chiusa da una parola vuota. Risposte:
/// "!re" (una riga di risultato), "!done", "!trap" (errore del comando), "!fatal" (connessione chiusa), "!empty" (v7.18+).
/// Una connessione per poll: aperta, login, comandi, chiusa (nessuna sessione da mantenere tra un ciclo e l'altro).
/// </summary>
public sealed class RouterOsApiClient : IAsyncDisposable
{
    private static readonly Encoding Text = Encoding.Latin1; // byte per byte: RouterOS non usa UTF-8 in modo uniforme

    private readonly TcpClient tcp;
    private readonly Stream stream;

    private RouterOsApiClient(TcpClient tcp, Stream stream) => (this.tcp, this.stream) = (tcp, stream);

    /// <summary>Connessione e login. Con <paramref name="useTls"/> e senza verifica, accetta certificati autofirmati.</summary>
    public static async Task<RouterOsApiClient> ConnectAsync(
        string host, int port, bool useTls, bool verifyCertificate, string username, string password, CancellationToken ct)
    {
        var tcp = new TcpClient { NoDelay = true };
        try
        {
            await tcp.ConnectAsync(host, port, ct);
            Stream stream = tcp.GetStream();
            if (useTls)
            {
                var ssl = new SslStream(stream, leaveInnerStreamOpen: false,
                    verifyCertificate ? null : (_, _, _, _) => true);
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, ct);
                stream = ssl;
            }

            var client = new RouterOsApiClient(tcp, stream);
            await client.LoginAsync(username, password, ct);
            return client;
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Login v6.43+ (nome e password in chiaro dentro la connessione); se il router risponde con una challenge
    /// (=ret=, RouterOS precedenti alla 6.43) completa il login MD5.
    /// </summary>
    private async Task LoginAsync(string username, string password, CancellationToken ct)
    {
        var reply = await RunAsync(["/login", $"=name={username}", $"=password={password}"], ct);
        if (reply.Done.TryGetValue("ret", out var challengeHex))
        {
            var challenge = Convert.FromHexString(challengeHex);
            var response = MD5.HashData([0, .. Text.GetBytes(password), .. challenge]);
            await RunAsync(["/login", $"=name={username}", $"=response=00{Convert.ToHexStringLower(response)}"], ct);
        }
    }

    public sealed record Reply(IReadOnlyList<IReadOnlyDictionary<string, string>> Rows, IReadOnlyDictionary<string, string> Done);

    /// <summary>Esegue un comando (es. "/system/resource/print") e raccoglie le righe fino a !done.</summary>
    /// <exception cref="RouterOsApiException">!trap o !fatal (es. "invalid user name or password").</exception>
    public async Task<Reply> RunAsync(IReadOnlyList<string> sentence, CancellationToken ct)
    {
        foreach (var word in sentence)
            await WriteWordAsync(word, ct);
        await WriteWordAsync("", ct);
        await stream.FlushAsync(ct);

        var rows = new List<IReadOnlyDictionary<string, string>>();
        string? trap = null;
        while (true)
        {
            var (type, attributes) = await ReadSentenceAsync(ct);
            switch (type)
            {
                case "!re":
                    rows.Add(attributes);
                    break;
                case "!trap":
                    trap ??= attributes.GetValueOrDefault("message", "errore del router");
                    break;
                case "!fatal":
                    throw new RouterOsApiException(attributes.GetValueOrDefault("message", "connessione chiusa dal router"));
                case "!done":
                    if (trap is not null)
                        throw new RouterOsApiException(trap);
                    return new Reply(rows, attributes);
                case "!empty":
                    break;
            }
        }
    }

    private async Task<(string Type, Dictionary<string, string> Attributes)> ReadSentenceAsync(CancellationToken ct)
    {
        var type = await ReadWordAsync(ct);
        var attributes = new Dictionary<string, string>();
        string? fatalMessage = null;
        while (await ReadWordAsync(ct) is { Length: > 0 } word)
        {
            // "=chiave=valore" (il valore può contenere "="); dopo !fatal la parola è il messaggio
            if (word[0] == '=' && word.IndexOf('=', 1) is var eq and > 0)
                attributes[word[1..eq]] = word[(eq + 1)..];
            else if (type == "!fatal")
                fatalMessage = word;
        }
        if (fatalMessage is not null)
            attributes.TryAdd("message", fatalMessage);
        return (type, attributes);
    }

    // ---------- Parole: lunghezza (1-5 byte) + contenuto ----------

    public static byte[] EncodeLength(int length) => length switch
    {
        < 0x80 => [(byte)length],
        < 0x4000 => [(byte)(length >> 8 | 0x80), (byte)length],
        < 0x200000 => [(byte)(length >> 16 | 0xC0), (byte)(length >> 8), (byte)length],
        < 0x10000000 => [(byte)(length >> 24 | 0xE0), (byte)(length >> 16), (byte)(length >> 8), (byte)length],
        _ => [0xF0, (byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length]
    };

    private async Task WriteWordAsync(string word, CancellationToken ct)
    {
        var bytes = Text.GetBytes(word);
        await stream.WriteAsync(EncodeLength(bytes.Length), ct);
        await stream.WriteAsync(bytes, ct);
    }

    private async Task<string> ReadWordAsync(CancellationToken ct)
    {
        var length = await ReadLengthAsync(ct);
        if (length > 16 * 1024 * 1024)
            throw new RouterOsApiException($"parola di {length} byte: risposta non valida");
        var buffer = new byte[length];
        await stream.ReadExactlyAsync(buffer, ct);
        return Text.GetString(buffer);
    }

    private async Task<int> ReadLengthAsync(CancellationToken ct)
    {
        var first = await ReadByteAsync(ct);
        var (extra, value) = first switch
        {
            < 0x80 => (0, first),
            < 0xC0 => (1, first & 0x3F),
            < 0xE0 => (2, first & 0x1F),
            < 0xF0 => (3, first & 0x0F),
            0xF0 => (4, 0),
            _ => throw new RouterOsApiException($"byte di controllo 0x{first:X2} non supportato")
        };
        for (var i = 0; i < extra; i++)
            value = value << 8 | await ReadByteAsync(ct);
        return value;
    }

    private readonly byte[] one = new byte[1];

    private async Task<int> ReadByteAsync(CancellationToken ct)
    {
        await stream.ReadExactlyAsync(one, ct);
        return one[0];
    }

    public async ValueTask DisposeAsync()
    {
        await stream.DisposeAsync();
        tcp.Dispose();
    }
}
