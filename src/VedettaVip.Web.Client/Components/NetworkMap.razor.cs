// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using VedettaVip.Shared.Models;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Components;

/// <summary>Strumento attivo in modalità modifica.</summary>
public enum MapTool { Move, Link }

/// <summary>Punto in coordinate della mappa (non dello schermo).</summary>
public readonly record struct MapPoint(double X, double Y);

/// <summary>Richiesta di un nuovo link, creata trascinando da un nodo a un altro.</summary>
public readonly record struct LinkRequest(Guid FromNodeId, Guid ToNodeId);

/// <summary>Clic destro su un nodo, alla posizione del puntatore nella finestra: apre il menu contestuale.</summary>
public readonly record struct NodeMenuRequest(MapNode Node, double ClientX, double ClientY);

public partial class NetworkMap : IAsyncDisposable
{
    // Sotto questa distanza (pixel schermo) un pointerdown/up è un clic, non un trascinamento
    private const double ClickTolerance = 4;

    /// <summary>
    /// Oltre questa età il traffico di un link non è più mostrato ("n/d"): 3 cicli di polling da 30 s.
    /// Stesso valore di Agent:TrafficStaleSeconds dell'API.
    /// </summary>
    public static readonly TimeSpan TrafficMaxAge = TimeSpan.FromSeconds(90);

    [Inject] private IJSRuntime JS { get; set; } = default!;

    [Parameter, EditorRequired] public List<MapNode> Nodes { get; set; } = [];
    [Parameter, EditorRequired] public List<MapLink> Links { get; set; } = [];
    [Parameter] public double GridSize { get; set; } = 20;

    /// <summary>False = sola visualizzazione (monitor NOC): pan e zoom, niente modifiche.</summary>
    [Parameter] public bool Editable { get; set; }
    [Parameter] public MapTool Tool { get; set; } = MapTool.Move;
    /// <summary>Nodo o link selezionato (gli Id sono univoci tra nodi e link).</summary>
    [Parameter] public Guid? SelectedId { get; set; }
    /// <summary>Punto scelto per l'inserimento di un nuovo nodo (mirino).</summary>
    [Parameter] public MapPoint? PlacePoint { get; set; }

    [Parameter] public EventCallback<MapNode> OnNodeMoved { get; set; }
    [Parameter] public EventCallback<MapNode> OnNodeOpen { get; set; }
    /// <summary>Clic su nodo o link (Id) oppure sullo sfondo (null). Solo in modifica.</summary>
    [Parameter] public EventCallback<Guid?> OnSelect { get; set; }
    [Parameter] public EventCallback<LinkRequest> OnLinkRequested { get; set; }
    /// <summary>Clic su un link in sola visualizzazione (senza trascinamento): apre il grafico del traffico.</summary>
    [Parameter] public EventCallback<MapLink> OnLinkOpen { get; set; }
    /// <summary>Clic destro su un nodo (anche in sola visualizzazione): menu contestuale.</summary>
    [Parameter] public EventCallback<NodeMenuRequest> OnNodeMenu { get; set; }
    /// <summary>Clic destro sullo sfondo: punto (già allineato alla griglia) per un nuovo nodo.</summary>
    [Parameter] public EventCallback<MapPoint> OnPlaceRequested { get; set; }

    private ElementReference svg;
    private IJSObjectReference? module;

    private double panX, panY, zoom = 1;
    private MapNode? dragNode;
    private bool panning;
    // Link premuto in sola visualizzazione: se il rilascio avviene senza trascinare, è un clic che apre il grafico
    private MapLink? pressedLink;
    private bool moved;
    private double startClientX, startClientY, startX, startY;
    private bool needsRender = true;

    // Creazione link: nodo di partenza, estremo che segue il puntatore, nodo di arrivo sotto il puntatore
    private MapNode? linkFrom;
    private MapNode? linkTarget;
    private double linkEndX, linkEndY;

    protected override void OnParametersSet() => needsRender = true;

    protected override bool ShouldRender()
    {
        var render = needsRender;
        needsRender = false;
        return render;
    }

    /// <summary>Centro dell'area visibile in coordinate mappa, allineato alla griglia.</summary>
    public async Task<MapPoint> GetViewCenterAsync()
    {
        var b = await GetBoundsAsync();
        return Snap((b.Width / 2 - panX) / zoom, (b.Height / 2 - panY) / zoom);
    }

    private MapNode? FindNode(Guid id) => Nodes.Find(n => n.Id == id);

    private void BeginPan(PointerEventArgs e)
    {
        panning = true;
        moved = false;
        (startClientX, startClientY, startX, startY) = (e.ClientX, e.ClientY, panX, panY);
    }

    private void OnBackgroundDown(PointerEventArgs e)
    {
        if (e.Button != 0) return;
        pressedLink = null;
        BeginPan(e);
    }

    private void OnNodeDown(PointerEventArgs e, MapNode node)
    {
        if (e.Button != 0)
            return;

        // In sola visualizzazione il nodo non si sposta: il trascinamento muove la vista
        if (!Editable)
        {
            BeginPan(e);
            return;
        }

        moved = false;
        (startClientX, startClientY, startX, startY) = (e.ClientX, e.ClientY, node.X, node.Y);

        if (Tool == MapTool.Link || e.ShiftKey)
        {
            linkFrom = node;
            linkTarget = null;
            (linkEndX, linkEndY) = (node.X, node.Y);
            needsRender = true;
        }
        else
        {
            dragNode = node;
        }
    }

    private async Task OnLinkDown(PointerEventArgs e, MapLink link)
    {
        if (e.Button != 0)
            return;

        if (Editable)
            await OnSelect.InvokeAsync(link.Id);
        else
        {
            BeginPan(e);
            pressedLink = link;
        }
    }

    private void OnNodeEnter(MapNode node)
    {
        if (linkFrom is null || node == linkFrom) return;
        linkTarget = node;
        needsRender = true;
    }

    private void OnNodeLeave(MapNode node)
    {
        if (linkTarget != node) return;
        linkTarget = null;
        needsRender = true;
    }

    private void OnPointerMove(PointerEventArgs e)
    {
        if (dragNode is null && linkFrom is null && !panning)
            return;

        var dx = e.ClientX - startClientX;
        var dy = e.ClientY - startClientY;
        if (Math.Abs(dx) > ClickTolerance || Math.Abs(dy) > ClickTolerance)
            moved = true;

        if (linkFrom is not null)
        {
            (linkEndX, linkEndY) = (startX + dx / zoom, startY + dy / zoom);
            needsRender = true;
        }
        else if (dragNode is not null)
        {
            if (!moved) return; // evita micro-spostamenti su un semplice clic
            dragNode.X = startX + dx / zoom;
            dragNode.Y = startY + dy / zoom;
            needsRender = true;
        }
        else
        {
            panX = startX + dx;
            panY = startY + dy;
            needsRender = true;
        }
    }

    private async Task OnPointerUp(PointerEventArgs e)
    {
        if (linkFrom is not null)
        {
            var (from, to) = (linkFrom, linkTarget);
            linkFrom = linkTarget = null;
            needsRender = true;

            if (to is not null)
                await OnLinkRequested.InvokeAsync(new LinkRequest(from.Id, to.Id));
            else if (!moved)
                await OnSelect.InvokeAsync(from.Id);
            return;
        }

        if (dragNode is not null)
        {
            var node = dragNode;
            dragNode = null;
            needsRender = true;

            if (moved)
            {
                (node.X, node.Y) = Snap(node.X, node.Y);
                await OnNodeMoved.InvokeAsync(node);
            }
            else
            {
                await OnSelect.InvokeAsync(node.Id);
            }
            return;
        }

        if (panning)
        {
            panning = false;
            var link = pressedLink;
            pressedLink = null;
            if (!moved && Editable)
                await OnSelect.InvokeAsync(null);
            else if (!moved && link is not null)
                await OnLinkOpen.InvokeAsync(link);
        }
    }

    /// <summary>Uscendo dalla mappa: il link in corso si annulla, il trascinamento del nodo si conclude.</summary>
    private async Task OnPointerLeave(PointerEventArgs e)
    {
        if (linkFrom is not null)
        {
            linkFrom = linkTarget = null;
            needsRender = true;
            return;
        }

        panning = false;
        if (dragNode is not null)
            await OnPointerUp(e);
    }

    private async Task OnContextMenu(MouseEventArgs e)
    {
        if (!Editable) return;

        var b = await GetBoundsAsync();
        var point = Snap((e.ClientX - b.Left - panX) / zoom, (e.ClientY - b.Top - panY) / zoom);
        await OnPlaceRequested.InvokeAsync(point);
    }

    private void OnWheel(WheelEventArgs e)
    {
        zoom = Math.Clamp(zoom * (e.DeltaY < 0 ? 1.1 : 1 / 1.1), 0.25, 4);
        needsRender = true;
    }

    private MapPoint Snap(double x, double y) =>
        new(Math.Round(x / GridSize) * GridSize, Math.Round(y / GridSize) * GridSize);

    private async Task<Bounds> GetBoundsAsync()
    {
        module ??= await JS.InvokeAsync<IJSObjectReference>("import", "./Components/NetworkMap.razor.js");
        return await module.InvokeAsync<Bounds>("bounds", svg);
    }

    public async ValueTask DisposeAsync()
    {
        if (module is null) return;
        try
        {
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
    }

    private sealed record Bounds(double Left, double Top, double Width, double Height);

    // InvariantCulture obbligatoria: con cultura it-IT i decimali avrebbero la virgola e l'SVG si rompe
    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static double Util(long bps, long speed) =>
        speed > 0 ? Math.Clamp((double)bps / speed, 0, 1) : 0;

    private static double LinkWidth(double u) => 2 + u * 8;

    private static string LinkColor(double u) => u switch
    {
        < 0.5 => "#2e9e44",
        < 0.8 => "#e0a800",
        _ => "#d62828"
    };

    /// <summary>Lato dell'icona del nodo e spazio orizzontale che occupa (margini compresi).</summary>
    private const int IconSize = 22, IconSpace = 30;

    /// <summary>Icone presenti sulla mappa, una volta sola: solo queste diventano symbol nei defs.</summary>
    private IEnumerable<MapIcon> UsedIcons() =>
        Nodes.Select(n => n.Icon).Distinct().Select(MapIcons.Find).OfType<MapIcon>();

    private static string StateColor(NodeState s) => s switch
    {
        NodeState.Up => "#8fd18f",
        NodeState.Partial => "#f5c06b",
        NodeState.Down => "#f08080",
        _ => "#c8c8c8"
    };

    internal static string Bps(long v) => v switch
    {
        >= 1_000_000_000 => $"{v / 1e9:0.0}G",
        >= 1_000_000 => $"{v / 1e6:0.0}M",
        >= 1_000 => $"{v / 1e3:0}k",
        _ => $"{v}"
    };
}
