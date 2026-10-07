// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Shared.Contracts;

namespace VedettaVip.Web.Client.Services;

public sealed record MapIcon(string Key, string Label, string Body);

/// <summary>
/// Icone di mappa e dispositivi (Tabler Icons, catalogo generato in MapIcons.g.cs). Icona effettiva di un nodo:
/// quella del nodo, poi quella del dispositivo, poi la predefinita del tipo (o della sottomappa). Una chiave sconosciuta
/// (icona tolta dal catalogo) ricade sulla predefinita.
/// </summary>
public static partial class MapIcons
{
    // Pigro: All sta nell'altro file partial e l'ordine di inizializzazione dei campi statici tra file non è garantito
    private static Dictionary<string, MapIcon>? byKey;
    private static Dictionary<string, MapIcon> ByKey => byKey ??= All.ToDictionary(i => i.Key);

    public static MapIcon? Find(string? key) => key is not null && ByKey.TryGetValue(key, out var icon) ? icon : null;

    public static string DefaultFor(DeviceType type) => type switch
    {
        DeviceType.Router => "router",
        DeviceType.Switch => "topology-bus",
        DeviceType.AccessPoint => "access-point",
        DeviceType.Server => "server",
        DeviceType.Firewall => "firewall-flame",
        DeviceType.Storage => "database",
        DeviceType.Pc => "device-desktop",
        DeviceType.Printer => "printer",
        DeviceType.Camera => "device-cctv",
        DeviceType.Phone => "device-landline-phone",
        DeviceType.Ups => "battery-charging",
        _ => "box"
    };

    /// <summary>Predefinita di un nodo: tipo del dispositivo, "sitemap" per le sottomappe, nessuna per i nodi statici.</summary>
    public static string? DefaultFor(MapNodeKind kind, DeviceType? type) => kind switch
    {
        MapNodeKind.Device => DefaultFor(type ?? DeviceType.Other),
        MapNodeKind.Submap => "sitemap",
        _ => null
    };

    /// <summary>Prima chiave presente nel catalogo, nell'ordine dato.</summary>
    public static string? Resolve(params string?[] keys) => keys.FirstOrDefault(k => Find(k) is not null);
}
