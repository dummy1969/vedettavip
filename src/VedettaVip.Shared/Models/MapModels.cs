// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Text.RegularExpressions;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Shared.Models;

public enum NodeState { Unknown, Up, Partial, Down }

public sealed class MapNode
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public MapNodeKind Kind { get; set; } = MapNodeKind.Device;
    public Guid? DeviceId { get; set; }
    public Guid? SubmapId { get; set; }
    public string LabelTemplate { get; set; } = "[Name]\n[Address]";
    public Dictionary<string, string> Values { get; } = new();
    public double X { get; set; }
    public double Y { get; set; }
    public NodeState State { get; set; } = NodeState.Unknown;
    /// <summary>Icona effettiva (chiave del catalogo; null = nessuna) e quella scelta sul nodo stesso.</summary>
    public string? Icon { get; set; }
    public string? OwnIcon { get; set; }
    public DeviceType? DeviceType { get; set; }
    /// <summary>Produttore del dispositivo (null per sottomappe e nodi statici).</summary>
    public DeviceVendor? Vendor { get; set; }
    /// <summary>Nome della finestra di manutenzione attiva (null = nessuna): bordo tratteggiato blu sulla mappa.</summary>
    public string? Maintenance { get; set; }

    /// <summary>Nodi Submap: dispositivi contenuti (anche nelle sottomappe discendenti) con il loro stato.</summary>
    public Dictionary<Guid, SubmapMemberDto> SubmapMembers { get; } = new();
    /// <summary>Conteggi dell'ultimo ricalcolo (null per i nodi non Submap).</summary>
    public SubmapCounts? SubmapCounts { get; private set; }

    /// <summary>Ricalcola stato aggregato, conteggi e variabili dell'etichetta ([Summary] [Down] [Partial] [Up] [Total]).</summary>
    public void RefreshSubmapState()
    {
        if (Kind != MapNodeKind.Submap)
            return;
        var c = SubmapStatus.Count(SubmapMembers.Values);
        SubmapCounts = c;
        State = SubmapStatus.Aggregate(c);
        Values["Summary"] = c.Summary();
        Values["Down"] = c.Down.ToString();
        Values["Partial"] = c.Partial.ToString();
        Values["Up"] = c.Up.ToString();
        Values["Total"] = c.Total.ToString();
    }

    /// <summary>Aggiorna lo stato di un dispositivo contenuto; false se il dispositivo non è nella sottomappa.</summary>
    public bool UpdateSubmapMember(Guid deviceId, NodeState state)
    {
        if (!SubmapMembers.TryGetValue(deviceId, out var member))
            return false;
        SubmapMembers[deviceId] = member with { State = state };
        RefreshSubmapState();
        return true;
    }
}

public sealed class MapLink
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid FromId { get; set; }
    public Guid ToId { get; set; }
    /// <summary>Dispositivo e interfaccia da cui leggere il traffico (tx = From → To).</summary>
    public Guid? DeviceId { get; set; }
    public int? IfIndex { get; set; }
    public long SpeedBps { get; set; } = 1_000_000_000;
    /// <summary>Soglia di utilizzo specifica in %: null = generale, 0 = disattivata.</summary>
    public int? UtilizationThresholdPct { get; set; }
    public long TxBps { get; set; }   // From -> To
    public long RxBps { get; set; }   // To -> From

    /// <summary>Istante dell'ultimo campione di traffico (null = mai ricevuto).</summary>
    public DateTimeOffset? TrafficTime { get; set; }
    /// <summary>ifName e velocità rilevati dall'agente, per verificare l'IfIndex nel pannello.</summary>
    public string? IfName { get; set; }
    public long? DetectedSpeedBps { get; set; }

    /// <summary>Il link ha una sorgente di traffico configurata.</summary>
    public bool IsMeasured => DeviceId is not null && IfIndex is not null;

    /// <summary>Dati di traffico recenti (più giovani di <paramref name="maxAge"/>).</summary>
    public bool HasTraffic(DateTimeOffset now, TimeSpan maxAge) =>
        IsMeasured && TrafficTime is { } t && now - t <= maxAge;
}

public static partial class LabelTemplate
{
    [GeneratedRegex(@"\[([A-Za-z0-9_.]+)\]")]
    private static partial Regex Token();

    public static string Render(string template, IReadOnlyDictionary<string, string> values) =>
        Token().Replace(template, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : "?");
}
