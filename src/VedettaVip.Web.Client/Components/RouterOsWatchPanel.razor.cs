// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Components;

/// <summary>
/// Interfacce e peer WireGuard dell'ultima lettura RouterOS di un dispositivo, con la casella "sorveglia" (salvata
/// subito, indipendente dal Salva del pannello). Prima le interfacce sorvegliate, poi le altre.
/// </summary>
public partial class RouterOsWatchPanel
{
    [Inject] private VedettaVipApiClient Api { get; set; } = default!;

    [Parameter, EditorRequired] public Guid DeviceId { get; set; }
    /// <summary>Numero di elementi sorvegliati (per il badge della scheda).</summary>
    [Parameter] public EventCallback<int> OnWatchCountChanged { get; set; }

    /// <summary>Allineato alla soglia dell'API (RouterOsWatchRules.WireGuardHandshakeMaxSeconds).</summary>
    private const int HandshakeMaxSeconds = 180;

    private DeviceRouterOsDetailDto? detail;
    private IReadOnlyList<RouterOsWatchDto> watches = [];
    private string filter = "";
    private bool busy;
    private string? error;

    protected override async Task OnParametersSetAsync()
    {
        if (detail?.DeviceId == DeviceId)
            return;
        try
        {
            detail = await Api.GetDeviceRouterOsDetailAsync(DeviceId, CancellationToken.None);
            watches = detail.Watches;
            await OnWatchCountChanged.InvokeAsync(watches.Count);
        }
        catch (HttpRequestException ex)
        {
            error = ex.Message;
        }
    }

    private RouterOsWatchDto? Watch(RouterOsWatchKindDto kind, string key) => watches.FirstOrDefault(w => w.Kind == kind && w.Key == key);

    private IEnumerable<WireGuardPeerDto> Peers() => detail?.Latest?.WireGuardPeers ?? [];

    private IEnumerable<RouterOsInterfaceDto> InterfacesFiltered()
    {
        var text = filter.Trim();
        return (detail?.Latest?.Interfaces ?? [])
            .Where(i => text.Length == 0 || i.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                        || i.Type?.Contains(text, StringComparison.OrdinalIgnoreCase) == true
                        || i.Comment?.Contains(text, StringComparison.OrdinalIgnoreCase) == true)
            .OrderBy(i => Watch(RouterOsWatchKindDto.Interface, i.Name) is null)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Sorvegliati che non compaiono più nella lettura (interfaccia rinominata, peer eliminato).</summary>
    private IEnumerable<RouterOsWatchDto> Orphans() => watches.Where(w => w.Kind == RouterOsWatchKindDto.Interface
        ? detail?.Latest?.Interfaces?.Any(i => i.Name == w.Key) != true
        : detail?.Latest?.WireGuardPeers is { } peers && peers.All(p => p.PublicKey != w.Key));

    private static string InterfaceLabel(RouterOsInterfaceDto i) => i.Comment is null ? i.Name : $"{i.Name} ({i.Comment})";

    private static string PeerLabel(WireGuardPeerDto p) => p.Name ?? p.Comment ?? p.Endpoint ?? p.PublicKey[..Math.Min(10, p.PublicKey.Length)] + "…";

    private static string PeerClass(WireGuardPeerDto p) =>
        p.Disabled ? "disabled" : p.LastHandshakeSeconds is { } s && s <= HandshakeMaxSeconds ? "up" : "down";

    private static string PeerText(WireGuardPeerDto p) =>
        p.Disabled ? "disabilitato" : p.LastHandshakeSeconds is { } s ? $"{RouterOsText.Uptime(s)} fa" : "mai";

    private async Task ToggleAsync(RouterOsWatchKindDto kind, string key, string label, bool watched)
    {
        busy = true;
        error = null;
        try
        {
            watches = await Api.ToggleRouterOsWatchAsync(DeviceId, new RouterOsWatchToggleDto(kind, key, label, watched), CancellationToken.None);
            await OnWatchCountChanged.InvokeAsync(watches.Count);
        }
        catch (HttpRequestException ex)
        {
            error = ex.Message;
        }
        finally
        {
            busy = false;
        }
    }
}
