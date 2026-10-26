// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace VedettaVip.Web.Client.Components;

/// <summary>
/// Menu contestuale dello sfondo della mappa in modalità Modifica (clic destro su un punto vuoto): aggiunta di un nodo nel
/// punto e "Metti in colonna le sottomappe", con conferma al secondo clic. Si chiude con un clic fuori o con Esc.
/// </summary>
public partial class MapMenu : IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>Posizione del puntatore nella finestra (clientX/clientY).</summary>
    [Parameter] public double X { get; set; }
    [Parameter] public double Y { get; set; }
    /// <summary>Nodi sottomappa della mappa: con meno di due il comando di incolonnamento è disattivato.</summary>
    [Parameter] public int SubmapCount { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }
    [Parameter] public EventCallback OnAddNode { get; set; }
    [Parameter] public EventCallback OnArrangeSubmaps { get; set; }

    private ElementReference menu;
    private IJSObjectReference? module;
    private bool arrangeConfirm;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;
        // Stessa funzione del menu dei nodi: dentro la finestra anche vicino ai bordi, e con il fuoco per chiudere con Esc
        module = await JS.InvokeAsync<IJSObjectReference>("import", "./Components/NodeMenu.razor.js");
        await module.InvokeVoidAsync("fit", menu);
    }

    private Task CloseAsync() => OnClose.InvokeAsync();

    private async Task OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
            await CloseAsync();
    }

    private async Task AddNodeAsync()
    {
        await CloseAsync();
        await OnAddNode.InvokeAsync();
    }

    private async Task ArrangeAsync()
    {
        await CloseAsync();
        await OnArrangeSubmaps.InvokeAsync();
    }

    private static string Px(double v) => v.ToString("0", CultureInfo.InvariantCulture) + "px";

    public async ValueTask DisposeAsync()
    {
        if (module is null)
            return;
        try
        {
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
