// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Components;

/// <summary>
/// Menu contestuale di un nodo della mappa (clic destro), alla posizione del puntatore: "Apri con WinBox" per i MikroTik,
/// copia dell'indirizzo, grafici o sottomappa, proprietà in modalità Modifica. Si chiude con un clic fuori o con Esc.
/// </summary>
public partial class NodeMenu : IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    [Parameter, EditorRequired] public MapNode Node { get; set; } = default!;
    /// <summary>Posizione del puntatore nella finestra (clientX/clientY).</summary>
    [Parameter] public double X { get; set; }
    [Parameter] public double Y { get; set; }
    /// <summary>Modalità Modifica: aggiunge "Proprietà del nodo".</summary>
    [Parameter] public bool Editing { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }
    /// <summary>Come il doppio clic: grafici del device o apertura della sottomappa.</summary>
    [Parameter] public EventCallback<MapNode> OnOpen { get; set; }
    [Parameter] public EventCallback<Guid?> OnProperties { get; set; }

    private ElementReference menu;
    private IJSObjectReference? module;

    private string Name => Node.Values.GetValueOrDefault("Name") is { Length: > 0 } name ? name : "Nodo";
    private string? Address => Node.Kind == MapNodeKind.Device ? Node.Values.GetValueOrDefault("Address") : null;
    private string? WinBoxHref => WinBoxLink.Href(Node.Vendor, Address);

    /// <summary>Un nodo statico in sola visualizzazione non ha voci: niente menu.</summary>
    private bool HasItems => Node.Kind != MapNodeKind.Static || Editing;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || !HasItems)
            return;
        // Dentro la finestra anche vicino ai bordi, e con il fuoco per chiudere con Esc
        module = await JS.InvokeAsync<IJSObjectReference>("import", "./Components/NodeMenu.razor.js");
        await module.InvokeVoidAsync("fit", menu);
    }

    private Task CloseAsync() => OnClose.InvokeAsync();

    private async Task OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
            await CloseAsync();
    }

    private async Task CopyAddressAsync()
    {
        if (module is not null && Address is { } address)
            await module.InvokeAsync<bool>("copyText", address);
        await CloseAsync();
    }

    private async Task OpenAsync()
    {
        await CloseAsync();
        await OnOpen.InvokeAsync(Node);
    }

    private async Task PropertiesAsync()
    {
        await CloseAsync();
        await OnProperties.InvokeAsync(Node.Id);
    }

    // InvariantCulture: con it-IT i decimali avrebbero la virgola e il CSS non sarebbe valido
    private static string Px(double v) => v.ToString("0.#", CultureInfo.InvariantCulture) + "px";

    public async ValueTask DisposeAsync()
    {
        if (module is null) return;
        try
        {
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
    }
}
