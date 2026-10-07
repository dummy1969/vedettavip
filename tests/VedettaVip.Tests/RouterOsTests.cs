// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using VedettaVip.Worker.Probes;

namespace VedettaVip.Tests;

/// <summary>Finto RouterOS in ascolto su localhost: parla il protocollo API vero (parole a lunghezza variabile).</summary>
internal sealed class FakeRouterOs : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly Task serving;
    public bool LegacyLogin { get; init; }
    public string Password { get; init; } = "segreta";
    public List<string[]> Received { get; } = [];
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public FakeRouterOs(Func<string[], List<string[]>> commands)
    {
        listener.Start();
        serving = ServeAsync(commands);
    }

    private async Task ServeAsync(Func<string[], List<string[]>> commands)
    {
        using var tcp = await listener.AcceptTcpClientAsync();
        var stream = tcp.GetStream();
        var challenge = RandomNumberGenerator.GetBytes(16);
        try
        {
            while (true)
            {
                var sentence = await ReadSentenceAsync(stream);
                Received.Add(sentence);
                List<string[]> reply;
                if (sentence[0] == "/login")
                {
                    if (LegacyLogin && !sentence.Any(w => w.StartsWith("=response=")))
                        reply = [["!done", "=ret=" + Convert.ToHexStringLower(challenge)]];
                    else if (LegacyLogin)
                    {
                        var expected = "00" + Convert.ToHexStringLower(MD5.HashData([0, .. Encoding.Latin1.GetBytes(Password), .. challenge]));
                        reply = sentence.Contains("=response=" + expected) ? [["!done"]] : [["!trap", "=message=invalid user name or password (6)"], ["!done"]];
                    }
                    else
                        reply = sentence.Contains("=password=" + Password) ? [["!done"]] : [["!trap", "=message=invalid user name or password (6)"], ["!done"]];
                }
                else
                    reply = commands(sentence);
                foreach (var s in reply)
                    await WriteSentenceAsync(stream, s);
            }
        }
        catch (Exception e) when (e is IOException or EndOfStreamException or ObjectDisposedException) { }
    }

    private static async Task<string[]> ReadSentenceAsync(Stream s)
    {
        var words = new List<string>();
        while (true)
        {
            var first = await ReadByte(s);
            int length = first switch
            {
                < 0x80 => first,
                < 0xC0 => (first & 0x3F) << 8 | await ReadByte(s),
                _ => throw new IOException("parola troppo lunga per il finto router")
            };
            if (length == 0) return [.. words];
            var buffer = new byte[length];
            await s.ReadExactlyAsync(buffer);
            words.Add(Encoding.Latin1.GetString(buffer));
        }
    }

    private static async Task<int> ReadByte(Stream s)
    {
        var b = new byte[1];
        await s.ReadExactlyAsync(b);
        return b[0];
    }

    private static async Task WriteSentenceAsync(Stream s, string[] words)
    {
        foreach (var w in words.Append(""))
        {
            var bytes = Encoding.Latin1.GetBytes(w);
            await s.WriteAsync(RouterOsApiClient.EncodeLength(bytes.Length));
            await s.WriteAsync(bytes);
        }
    }

    public async ValueTask DisposeAsync()
    {
        listener.Stop();
        await Task.WhenAny(serving, Task.Delay(1000));
    }
}

public class RouterOsTests
{
    private static readonly Guid Device = Guid.NewGuid();

    private static List<string[]> V7(string[] command) => command[0] switch
    {
        "/system/resource/print" => [["!re", "=uptime=2w3d4h5m6s", "=version=7.15.3 (stable)", "=cpu-load=12", "=free-memory=805306368",
                                      "=total-memory=1073741824", "=board-name=CCR2004-1G-12S+2XS", "=architecture-name=arm64"], ["!done"]],
        "/system/health/print" => [["!re", "=.id=*D", "=name=voltage", "=value=24.1", "=type=V"],
                                   ["!re", "=.id=*E", "=name=cpu-temperature", "=value=52", "=type=C"],
                                   ["!re", "=.id=*F", "=name=fan1-speed", "=value=2400", "=type=RPM"], ["!done"]],
        "/interface/print" => [["!re", "=name=ether1-wan", "=type=ether", "=running=true", "=disabled=false", "=comment=Fibra"],
                               ["!re", "=name=wg-sede", "=type=wg", "=running=true", "=disabled=false"],
                               ["!re", "=name=ether5", "=type=ether", "=running=false", "=disabled=true"], ["!done"]],
        "/interface/wireguard/peers/print" => [["!re", "=public-key=AbC+key=", "=interface=wg-sede", "=name=filiale",
                                                "=current-endpoint-address=203.0.113.7", "=current-endpoint-port=13231", "=last-handshake=1m12s",
                                                "=disabled=false"], ["!re", "=public-key=XyZ+key=", "=interface=wg-sede", "=comment=magazzino",
                                                "=endpoint-address=", "=disabled=false"], ["!done"]],
        _ => [["!trap", "=message=no such command"], ["!done"]]
    };

    private static async Task<RouterOsApiClient> Connect(FakeRouterOs router, string password = "segreta")
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return await RouterOsApiClient.ConnectAsync("127.0.0.1", router.Port, false, false, "vedettavip", password, timeout.Token);
    }

    [Fact]
    public async Task Reads_resources_and_v7_health_after_login()
    {
        await using var router = new FakeRouterOs(V7);
        await using var api = await Connect(router);
        var s = await RouterOsReader.ReadAsync(api, Device, DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.True(s.Ok);
        Assert.Equal((12d, 25.0, 2 * 604_800 + 3 * 86_400 + 4 * 3600 + 5 * 60 + 6L), (s.CpuLoadPct!.Value, s.MemoryUsedPct!.Value, s.UptimeSeconds!.Value));
        Assert.Equal(("7.15.3 (stable)", "CCR2004-1G-12S+2XS", "arm64"), (s.Version, s.BoardName, s.Architecture));
        Assert.Equal((52d, 24.1), (s.TemperatureC!.Value, s.VoltageV!.Value));
        Assert.Equal(3, s.Interfaces!.Count);
        Assert.Equal(new VedettaVip.Shared.Contracts.RouterOsInterfaceDto("ether1-wan", "ether", true, false, "Fibra"), s.Interfaces[0]);
        Assert.True(s.Interfaces[2].Disabled);
        var peer = s.WireGuardPeers![0];
        Assert.Equal(("filiale", "203.0.113.7:13231", 72L), (peer.Name, peer.Endpoint, peer.LastHandshakeSeconds!.Value));
        Assert.Equal(("magazzino", (string?)null, (long?)null), (s.WireGuardPeers[1].Comment, s.WireGuardPeers[1].Endpoint, s.WireGuardPeers[1].LastHandshakeSeconds));
        Assert.Equal(["/login", "=name=vedettavip", "=password=segreta"], router.Received[0]);
    }

    [Fact]
    public async Task Wrong_password_is_reported_as_router_error()
    {
        await using var router = new FakeRouterOs(V7);
        var ex = await Assert.ThrowsAsync<RouterOsApiException>(() => Connect(router, "sbagliata"));
        Assert.Contains("invalid user name or password", ex.Message);
    }

    [Fact]
    public async Task Legacy_v6_challenge_login()
    {
        await using var router = new FakeRouterOs(V7) { LegacyLogin = true };
        await using var api = await Connect(router);
        Assert.StartsWith("=response=00", router.Received[1][2]);
        Assert.True((await RouterOsReader.ReadAsync(api, Device, DateTimeOffset.UtcNow, CancellationToken.None)).Ok);
    }

    [Fact]
    public async Task Missing_health_command_is_not_an_error()
    {
        // Router v6 senza sensori: health e WireGuard non esistono, le interfacce sì
        await using var router = new FakeRouterOs(c => c[0] is "/system/resource/print" or "/interface/print" ? V7(c) : [["!trap", "=message=no such command prefix"], ["!done"]]);
        await using var api = await Connect(router);
        var s = await RouterOsReader.ReadAsync(api, Device, DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.True(s.Ok);
        Assert.Null(s.TemperatureC);
        Assert.Equal(12d, s.CpuLoadPct);
        Assert.Null(s.WireGuardPeers);
        Assert.Equal(3, s.Interfaces!.Count);
    }

    [Fact]
    public void V6_health_as_attributes()
    {
        var values = RouterOsReader.HealthValues([new Dictionary<string, string> { ["voltage"] = "23.9", ["temperature"] = "41", ["state"] = "ok" }]);
        Assert.Equal((23.9, 41d), (values["voltage"], values["temperature"]));
        Assert.False(values.ContainsKey("state"));
    }

    [Fact]
    public void Target_text_never_contains_the_routeros_password()
    {
        var target = new VedettaVip.Shared.Contracts.AgentTargetDto(Device, "core", "10.0.0.1", VedettaVip.Shared.Contracts.SnmpVersion.None,
            RouterOs: new VedettaVip.Shared.Contracts.RouterOsTargetDto("vedettavip", "p4ssw0rd", true, 8729, false));
        Assert.DoesNotContain("p4ssw0rd", target.ToString());
        Assert.DoesNotContain("p4ssw0rd", target.RouterOs!.ToString());
        Assert.Contains("8729", target.RouterOs.ToString());
    }

    [Fact]
    public void Memory_used_percentage()
    {
        var s = new VedettaVip.Shared.Contracts.RouterOsSampleDto(Device, DateTimeOffset.UtcNow, true, MemoryTotalBytes: 1000, MemoryFreeBytes: 250);
        Assert.Equal(75.0, s.MemoryUsedPct);
        Assert.Null((s with { MemoryTotalBytes = null }).MemoryUsedPct);
    }

    [Theory]
    [InlineData("2w3d4h5m6s", 2 * 604_800 + 3 * 86_400 + 4 * 3600 + 5 * 60 + 6)]
    [InlineData("5h", 5 * 3600)]
    [InlineData("45s", 45)]
    [InlineData("1d02:03:04", 86_400 + 2 * 3600 + 3 * 60 + 4)]
    [InlineData("02:03:04", 2 * 3600 + 3 * 60 + 4)]
    public void Parses_uptime_formats(string text, long seconds) => Assert.Equal(seconds, RouterOsReader.ParseUptime(text));

    [Theory]
    [InlineData("")]
    [InlineData("ieri")]
    [InlineData("5x")]
    public void Unknown_uptime_is_null(string text) => Assert.Null(RouterOsReader.ParseUptime(text));

    [Theory]
    [InlineData(0, new byte[] { 0 })]
    [InlineData(0x7F, new byte[] { 0x7F })]
    [InlineData(0x80, new byte[] { 0x80, 0x80 })]
    [InlineData(0x3FFF, new byte[] { 0xBF, 0xFF })]
    [InlineData(0x4000, new byte[] { 0xC0, 0x40, 0x00 })]
    [InlineData(0x200000, new byte[] { 0xE0, 0x20, 0x00, 0x00 })]
    [InlineData(0x10000000, new byte[] { 0xF0, 0x10, 0x00, 0x00, 0x00 })]
    public void Encodes_word_lengths(int length, byte[] expected) => Assert.Equal(expected, RouterOsApiClient.EncodeLength(length));
}
