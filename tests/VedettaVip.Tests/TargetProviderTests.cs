// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using VedettaVip.Shared.Contracts;
using VedettaVip.Worker.Api;
using VedettaVip.Worker.Options;
using VedettaVip.Worker.Services;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace VedettaVip.Tests;

public class TargetProviderTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-00000000000c");

    /// <summary>Risponde a GET /api/agent/targets con gli elenchi dati, uno per chiamata (l'ultimo si ripete).</summary>
    private sealed class SequenceHandler(params List<AgentTargetDto>[] responses) : HttpMessageHandler
    {
        private int calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var i = Math.Min(Interlocked.Increment(ref calls) - 1, responses.Length - 1);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(responses[i], options: Json)
            });
        }
    }

    private static TargetProvider NewProvider(SequenceHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") };
        var api = new AgentApiClient(http, NullLogger<AgentApiClient>.Instance);
        // Refresh periodico lunghissimo: nel test i refresh arrivano solo da RequestRefresh
        var options = MsOptions.Create(new AgentOptions { ApiKey = new string('k', 32), TargetsRefreshSeconds = 3600 });
        return new TargetProvider(api, options, NullLogger<TargetProvider>.Instance);
    }

    private static async Task<AgentTargetDto> ReadChangeAsync(TargetProvider provider)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return await provider.Changes.ReadAsync(timeout.Token);
    }

    [Fact]
    public async Task Publishes_only_new_targets_and_address_or_snmp_changes_after_the_first_load()
    {
        var handler = new SequenceHandler(
            // 1° caricamento: nessun cambio pubblicato (ci pensa il ciclo normale)
            [new(A, "router", "10.0.0.1", SnmpVersion.V2c), new(B, "switch", "10.0.0.2", SnmpVersion.V2c)],
            // 2°: A rinominato (non conta), C nuovo
            [new(A, "router-core", "10.0.0.1", SnmpVersion.V2c), new(B, "switch", "10.0.0.2", SnmpVersion.V2c),
             new(C, "ap", "10.0.0.3", SnmpVersion.None)],
            // 3°: A cambia indirizzo, B cambia versione SNMP, C invariato
            [new(A, "router-core", "10.0.0.11", SnmpVersion.V2c), new(B, "switch", "10.0.0.2", SnmpVersion.V1),
             new(C, "ap", "10.0.0.3", SnmpVersion.None)]);

        using var provider = NewProvider(handler);
        await provider.StartAsync(CancellationToken.None);
        try
        {
            for (var i = 0; i < 250 && provider.Current is null; i++)
                await Task.Delay(20);
            Assert.Equal(2, provider.Current?.Count);
            Assert.False(provider.Changes.TryRead(out _));

            provider.RequestRefresh();
            Assert.Equal(C, (await ReadChangeAsync(provider)).DeviceId);
            Assert.False(provider.Changes.TryRead(out _)); // il solo cambio di nome non viene pubblicato
            Assert.Equal(3, provider.Current?.Count);

            provider.RequestRefresh();
            var first = await ReadChangeAsync(provider);
            var second = await ReadChangeAsync(provider);
            Assert.Equal((A, "10.0.0.11"), (first.DeviceId, first.Address));
            Assert.Equal((B, SnmpVersion.V1), (second.DeviceId, second.SnmpVersion));
            Assert.False(provider.Changes.TryRead(out _));
        }
        finally
        {
            await provider.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Publishes_a_change_of_snmp_community()
    {
        var handler = new SequenceHandler(
            [new(A, "router", "10.0.0.1", SnmpVersion.V2c, SnmpCommunity: "vecchia")],
            [new(A, "router", "10.0.0.1", SnmpVersion.V2c, SnmpCommunity: "nuova")]);

        using var provider = NewProvider(handler);
        await provider.StartAsync(CancellationToken.None);
        try
        {
            for (var i = 0; i < 250 && provider.Current is null; i++)
                await Task.Delay(20);
            provider.RequestRefresh();
            Assert.Equal("nuova", (await ReadChangeAsync(provider)).SnmpCommunity);
        }
        finally
        {
            await provider.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void Target_text_never_contains_the_snmp_community()
    {
        var text = new AgentTargetDto(A, "router", "10.0.0.1", SnmpVersion.V2c, SnmpCommunity: "s3gr3ta").ToString();
        Assert.DoesNotContain("s3gr3ta", text);
        Assert.Contains("10.0.0.1", text);
    }

    [Fact]
    public void Repeated_refresh_requests_do_not_throw()
    {
        using var provider = NewProvider(new SequenceHandler([]));
        for (var i = 0; i < 5; i++)
            provider.RequestRefresh();
    }
}
