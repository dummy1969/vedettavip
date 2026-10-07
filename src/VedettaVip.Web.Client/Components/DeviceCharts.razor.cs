// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using VedettaVip.Shared.Contracts;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Components;

/// <summary>
/// Grafici di un device: latenza e perdita dei ping, traffico delle interfacce che hanno storico (dal punto di
/// vista dell'interfaccia: Out = uscita, In = ingresso). Usato nella pagina Dispositivi e sulla mappa (doppio clic).
/// </summary>
public partial class DeviceCharts : IDisposable
{
    [Inject] private VedettaVipApiClient Api { get; set; } = default!;
    [Inject] private ILogger<DeviceCharts> Logger { get; set; } = default!;

    [Parameter, EditorRequired] public Guid DeviceId { get; set; }
    [Parameter] public string? Title { get; set; }
    [Parameter] public string? Address { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private readonly CancellationTokenSource cts = new();
    private Guid? loadedFor;
    private IReadOnlyList<InterfaceWithHistoryDto>? interfaces;
    private int? selectedIfIndex;
    private bool routerOsEnabled;

    protected override async Task OnParametersSetAsync()
    {
        if (loadedFor == DeviceId)
            return;

        loadedFor = DeviceId;
        interfaces = null;
        selectedIfIndex = null;
        routerOsEnabled = false;
        try
        {
            routerOsEnabled = (await Api.GetDeviceAsync(DeviceId, cts.Token)).RouterOsApiEnabled;
            interfaces = await Api.GetInterfacesWithHistoryAsync(DeviceId, cts.Token);
            selectedIfIndex = interfaces.FirstOrDefault()?.IfIndex;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning("Interfacce con storico non caricate: {Message}", ex.Message);
            interfaces = [];
        }
    }

    private static string InterfaceText(InterfaceWithHistoryDto i)
    {
        var text = $"{i.IfIndex} — {i.Name ?? "?"}";
        if (i.Alias is { Length: > 0 } alias && alias != i.Name)
            text += $" · {alias}";
        if (i.SpeedBps is { } speed)
            text += $" ({NetworkMap.Bps(speed)})";
        return text;
    }

    public void Dispose()
    {
        cts.Cancel();
        cts.Dispose();
    }
}
