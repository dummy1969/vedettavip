// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Services;

/// <summary>
/// Dalle letture dei vicini alle proposte della Discovery (funzione pura, coperta da test):
/// <list type="bullet">
/// <item>vicino riconosciuto come device già in VedettaVip (stesso indirizzo, o stesso nome se univoco) → link mancante,
/// se i due non sono già collegati; i due lati dello stesso cavo diventano una sola proposta;</item>
/// <item>vicino sconosciuto → dispositivo da aggiungere, con tipo dedotto e suggerimenti presi da chi l'ha visto.</item>
/// </list>
/// </summary>
public static class DiscoveryPlanner
{
    public sealed record DeviceInfo(Guid Id, string Name, string Address, Guid? CustomerId, bool HasInterfaces);
    public sealed record NodeInfo(Guid DeviceId, Guid MapId, string MapName);
    public sealed record LinkInfo(Guid FromDeviceId, Guid ToDeviceId);
    public sealed record InterfaceInfo(Guid DeviceId, int IfIndex, string? Name, long? SpeedBps);
    public sealed record NeighborInfo(Guid DeviceId, NeighborDto Neighbor);
    public sealed record ArpInfo(Guid RouterId, ArpEntryDto Entry);
    public sealed record ScanHostInfo(Guid ScanId, Guid? CustomerId, Guid? MapId, ScanHostDto Host);

    /// <summary>Un'osservazione di un host sconosciuto: da un vicino, dall'ARP di un router o da una scansione.</summary>
    private sealed record Observation(string Key, string? Address, string? Mac, NeighborDto? Neighbor = null, ArpEntryDto? Arp = null,
        ScanHostInfo? Scan = null, DeviceInfo? Seer = null);

    public const long DefaultSpeedBps = 1_000_000_000;

    public static (List<DiscoveryDeviceProposalDto> Devices, List<DiscoveryLinkProposalDto> Links, int Ignored) Plan(
        IReadOnlyList<DeviceInfo> devices, IReadOnlyList<NodeInfo> nodes, IReadOnlyList<LinkInfo> links,
        IReadOnlyList<InterfaceInfo> interfaces, IReadOnlyList<NeighborInfo> neighbors, IReadOnlySet<string> ignored,
        IReadOnlyList<ArpInfo>? arp = null, IReadOnlyList<ScanHostInfo>? scans = null)
    {
        var byId = devices.ToDictionary(d => d.Id);
        var addressLookup = devices.ToLookup(d => d.Address, StringComparer.OrdinalIgnoreCase);
        var byName = devices.GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.Single(), StringComparer.OrdinalIgnoreCase);
        var mapsOf = nodes.GroupBy(n => n.DeviceId).ToDictionary(g => g.Key, g => g.OrderBy(n => n.MapName, StringComparer.CurrentCultureIgnoreCase).ToList());
        var linked = links.Select(l => Pair(l.FromDeviceId, l.ToDeviceId)).ToHashSet();
        var ifaces = interfaces.ToLookup(i => i.DeviceId);

        var sightings = new List<(DeviceInfo Seer, NeighborDto N, DeviceInfo? Known)>();
        foreach (var (seerId, n) in neighbors)
        {
            if (!byId.TryGetValue(seerId, out var seer))
                continue;
            var known = (n.Address is { } a ? addressLookup[a].FirstOrDefault(d => Compatible(d.CustomerId, seer.CustomerId)) : null)
                        ?? (n.Identity is { } id ? byName.GetValueOrDefault(id) : null);
            if (known?.Id == seer.Id)
                continue; // un router che vede se stesso (bridge, VLAN)
            sightings.Add((seer, n, known));
        }

        var ignoredCount = 0;

        // ---------- Link fra device noti ----------
        var linkProposals = new List<DiscoveryLinkProposalDto>();
        foreach (var group in sightings.Where(s => s.Known is not null).GroupBy(s => Pair(s.Seer.Id, s.Known!.Id)))
        {
            if (linked.Contains(group.Key))
                continue;
            // Lati del cavo dal punto di vista del device con Id minore (A) e dell'altro (B)
            var (aId, bId) = group.Key;
            var sides = group.Select(s => s.Seer.Id == aId
                    ? (A: LocalName(s.N, s.Seer, ifaces), B: s.N.RemoteInterface, s.N.Protocol, ReportedBy: s.Seer.Id, s.N.LocalIfIndex)
                    : (A: s.N.RemoteInterface, B: LocalName(s.N, s.Seer, ifaces), s.N.Protocol, ReportedBy: s.Seer.Id, s.N.LocalIfIndex))
                .ToList();
            foreach (var cable in Cables(sides.Select(s => (s.A, s.B)).ToList()))
            {
                var a = byId[aId];
                var b = byId[bId];
                var protocols = string.Join(',', sides.Where(s => Same(s.A, cable.A) || Same(s.B, cable.B))
                    .SelectMany(s => s.Protocol.Split(',')).Select(p => p.Trim()).Distinct().Order());
                var key = $"link:{aId}|{Norm(cable.A)}|{bId}|{Norm(cable.B)}";
                if (ignored.Contains(key)) { ignoredCount++; continue; }

                var common = mapsOf.GetValueOrDefault(aId)?.FirstOrDefault(m => mapsOf.GetValueOrDefault(bId)?.Any(x => x.MapId == m.MapId) == true);
                var source = Source(a, cable.A, ifaces) ?? Source(b, cable.B, ifaces);
                linkProposals.Add(new DiscoveryLinkProposalDto(key, aId, a.Name, cable.A, bId, b.Name, cable.B,
                    common?.MapId, common?.MapName, source?.DeviceId, source?.IfIndex, source?.SpeedBps ?? DefaultSpeedBps, protocols,
                    common is null ? "nessuna mappa in comune: aggiungere i due dispositivi alla stessa mappa" : null));
            }
        }

        // ---------- Dispositivi sconosciuti: vicini, ARP e scansioni uniti per indirizzo ----------
        var macToIp = (arp ?? []).Where(x => x.Entry.MacAddress is not null)
            .GroupBy(x => x.Entry.MacAddress!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Entry.Address, StringComparer.OrdinalIgnoreCase);
        var observations = new List<Observation>();
        foreach (var s in sightings.Where(s => s.Known is null))
        {
            var address = s.N.Address ?? (s.N.MacAddress is { } m ? macToIp.GetValueOrDefault(m) : null);
            if (address is not null && IsKnown(address, s.Seer.CustomerId))
                continue; // già in VedettaVip con un altro nome
            observations.Add(new Observation(Key(address, s.N.MacAddress, s.N.Identity), address, s.N.MacAddress, Neighbor: s.N, Seer: s.Seer));
        }
        foreach (var (routerId, e) in arp ?? [])
            if (byId.TryGetValue(routerId, out var router) && !IsKnown(e.Address, router.CustomerId))
                observations.Add(new Observation(Key(e.Address, null, null), e.Address, e.MacAddress, Arp: e, Seer: router));
        foreach (var h in scans ?? [])
            if (!IsKnown(h.Host.Address, h.CustomerId))
                observations.Add(new Observation(Key(h.Host.Address, null, null), h.Host.Address, null, Scan: h));

        var deviceProposals = new List<DiscoveryDeviceProposalDto>();
        foreach (var (key, legacyKey, obs) in SplitByMac(observations))
        {
            if (ignored.Contains(key) || ignored.Contains(legacyKey)) { ignoredCount++; continue; }
            var n = obs.Select(o => o.Neighbor).OfType<NeighborDto>().OrderByDescending(x => x.Identity is not null).FirstOrDefault();
            var e = obs.Select(o => o.Arp).OfType<ArpEntryDto>().OrderByDescending(x => x.Comment is not null || x.HostName is not null).FirstOrDefault();
            var h = obs.Select(o => o.Scan).OfType<ScanHostInfo>().FirstOrDefault();
            var neighborSeer = obs.FirstOrDefault(o => o.Neighbor is not null)?.Seer;
            var arpSeer = obs.FirstOrDefault(o => o.Arp is not null)?.Seer;
            var address = obs.Select(o => o.Address).FirstOrDefault(x => x is not null);
            var mac = n?.MacAddress ?? e?.MacAddress;
            var ports = h?.Host.OpenPorts ?? [];
            var names = new[] { n?.Identity, h?.Host.SysName, e?.Comment, e?.HostName, h?.Host.DnsName };
            var vendor = MacVendors.Lookup(mac);
            var type = n is not null && GuessType(n) is var t && t != DeviceType.Other ? t : GuessHostType(h?.Host.SysDescr, ports, names);
            if (type == DeviceType.Other)
                type = VendorType(vendor, ports);

            var sources = new List<string>();
            if (n is not null) sources.Add("vicini");
            if (e is not null) sources.Add("arp");
            if (h is not null) sources.Add("scansione");

            var seenBy = obs.Where(o => o.Neighbor is not null)
                .Select(o => new DiscoverySightingDto(o.Seer!.Id, o.Seer.Name, LocalName(o.Neighbor!, o.Seer, ifaces), o.Neighbor!.RemoteInterface, o.Neighbor.Protocol))
                .Concat(obs.Where(o => o.Arp is not null)
                    .Select(o => new DiscoverySightingDto(o.Seer!.Id, o.Seer.Name, o.Arp!.Interface ?? IfName(o.Seer.Id, o.Arp.IfIndex, ifaces), null, "arp")))
                .DistinctBy(x => (x.DeviceId, x.LocalInterface, x.Protocol)).ToList();

            // Scansione scelta dall'operatore: suoi cliente e mappa; altrimenti quelli del router che l'ha visto
            var suggestFrom = neighborSeer ?? arpSeer;
            var customer = h?.CustomerId ?? suggestFrom?.CustomerId;
            var map = h?.MapId ?? (suggestFrom is null ? null : mapsOf.GetValueOrDefault(suggestFrom.Id)?.FirstOrDefault()?.MapId);

            deviceProposals.Add(new DiscoveryDeviceProposalDto(
                key,
                names.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) is { } named ? ShortName(named)
                    : Model(n?.Platform) ?? Model(FirstLine(h?.Host.SysDescr))
                      ?? (vendor is not null && vendor != MacVendors.PrivateMac && address is not null ? $"{vendor} {address}" : null)
                      ?? address ?? mac ?? "?",
                address,
                mac,
                type,
                (n is not null && IsRouterOs(n)) || h?.Host.SysDescr?.Contains("RouterOS", StringComparison.OrdinalIgnoreCase) == true
                    || ports.Contains(8728) || ports.Contains(8729),
                n?.Platform ?? FirstLine(h?.Host.SysDescr), n?.Version, n?.Board,
                seenBy,
                customer,
                map,
                suggestFrom?.Id,
                address is null ? "nessun indirizzo IPv4 annunciato: inserirlo a mano" : null,
                string.Join(',', sources),
                FirstLine(h?.Host.SysDescr) ?? e?.HostName ?? h?.Host.DnsName,
                ports,
                h is not null && (h.Host.SnmpProfileId is not null || h.Host.SnmpFallback),
                h?.Host.SnmpProfileId,
                vendor,
                h?.ScanId));
        }

        return (deviceProposals.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
                linkProposals.OrderBy(l => l.FromName, StringComparer.CurrentCultureIgnoreCase).ThenBy(l => l.ToName).ToList(),
                ignoredCount);

        // Indirizzo già di un device: dello stesso cliente (o senza cliente da una delle due parti), altrimenti è
        // un host omonimo di un'altra sede con la stessa rete
        bool IsKnown(string address, Guid? customerId) => addressLookup[address].Any(d => Compatible(d.CustomerId, customerId));
        static bool Compatible(Guid? a, Guid? b) => a is null || b is null || a == b;
    }

    /// <summary>
    /// Raggruppa le osservazioni per indirizzo. Stesso IP con MAC diversi = reti uguali in sedi diverse (la stessa rete privata
    /// in più sedi): un host per MAC, chiave "dev:IP|mac". Chi non ha MAC (scansione, vicino senza MAC) va con l'unico gruppo
    /// visto da router dello stesso cliente; se non è univoco resta a parte ("dev:IP|-"): meglio due righe che un host
    /// con nome e porte di un altro. LegacyKey = chiave "dev:IP" di prima, perché gli "Ignora" già dati valgano ancora.
    /// </summary>
    private static IEnumerable<(string Key, string LegacyKey, List<Observation> Obs)> SplitByMac(List<Observation> observations)
    {
        foreach (var bucket in observations.GroupBy(o => o.Key))
        {
            var macs = bucket.Select(o => o.Mac).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (bucket.First().Address is null || macs <= 1)
            {
                yield return (bucket.Key, bucket.Key, bucket.ToList());
                continue;
            }
            var byMac = bucket.Where(o => o.Mac is not null)
                .GroupBy(o => o.Mac!, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
            var orphans = new List<Observation>();
            foreach (var o in bucket.Where(o => o.Mac is null))
            {
                var customer = o.Scan?.CustomerId ?? o.Seer?.CustomerId;
                var same = customer is null ? [] : byMac.Values.Where(g => g.Any(x => x.Seer?.CustomerId == customer)).ToList();
                if (same.Count == 1) same[0].Add(o); else orphans.Add(o);
            }
            foreach (var (mac, list) in byMac)
                yield return ($"{bucket.Key}|{mac.ToLowerInvariant()}", bucket.Key, list);
            if (orphans.Count > 0)
                yield return ($"{bucket.Key}|-", bucket.Key, orphans);
        }
    }

    /// <summary>
    /// Chiave della proposta di un device sconosciuto: indirizzo (unisce vicini, ARP e scansioni), altrimenti MAC,
    /// altrimenti nome.
    /// </summary>
    public static string DeviceKey(NeighborDto n) => Key(n.Address, n.MacAddress, n.Identity);

    private static string Key(string? address, string? mac, string? identity) =>
        "dev:" + (address ?? mac ?? identity ?? "?").ToLowerInvariant();

    /// <summary>
    /// Tipo di un host senza vicini (ARP, scansione): da sysDescr, porte aperte e nomi (DHCP, DNS). Le porte valgono come
    /// indizio solo se sysDescr non dice già cos'è.
    /// </summary>
    public static DeviceType GuessHostType(string? sysDescr, IReadOnlyList<int> ports, IEnumerable<string?> names)
    {
        var d = (sysDescr ?? "").ToLowerInvariant();
        var n = string.Join(' ', names.Where(x => x is not null)).ToLowerInvariant();
        bool Any(string text, params string[] words) => words.Any(text.Contains);

        if (Any(d, "routeros") || ports.Contains(8728) || ports.Contains(8729)) return DeviceType.Router;
        if (Any(d, "printer", "laserjet", "officejet", "mfp", "brother", "kyocera", "lexmark", "xerox", "ricoh", "canon", "epson", "konica")
            || Any(n, "printer", "stampante", "prn-", "mfp")) return DeviceType.Printer;
        if (Any(d, "hikvision", "dahua", "ds-2cd", "ip camera", "network camera", "axis") || Any(n, "cam", "nvr", "dvr")) return DeviceType.Camera;
        if (Any(d, "smart-ups", "apc ", "ups", "eaton", "riello")) return DeviceType.Ups;
        if (Any(d, "synology", "qnap", "diskstation", "nas", "truenas") || Any(n, "nas")) return DeviceType.Storage;
        if (Any(d, "yealink", "fanvil", "snom", "grandstream", "sip phone", "voip") || Any(n, "sip-", "yealink", "phone", "tel-")) return DeviceType.Phone;
        if (Any(d, "esxi", "vmware", "proxmox", "hyper-v", "windows server")) return DeviceType.Server;
        if (Any(d, "fortigate", "fortinet", "pfsense", "opnsense", "sonicwall", "watchguard", "firewall")) return DeviceType.Firewall;
        if (Any(d, "switch", "procurve", "catalyst", "edgeswitch", "officeconnect")) return DeviceType.Switch;
        if (Any(d, "access point", "unifi", "wap") || Any(n, "ap-", "-ap")) return DeviceType.AccessPoint;
        if (Any(d, "cisco ios")) return DeviceType.Router;
        if (Any(d, "windows") || ports.Contains(3389)) return DeviceType.Pc;
        if (ports.Contains(9100)) return DeviceType.Printer;
        if (ports.Contains(554)) return DeviceType.Camera;
        return DeviceType.Other;
    }

    /// <summary>
    /// Tipo dedotto da piattaforma, modello e capacità annunciate: RouterOS CRS/CSS = switch, cAP/wAP = access point;
    /// capacità LLDP/CDP (telephone, wlan-ap, bridge senza router); modelli Cisco WS-C/Catalyst = switch.
    /// </summary>
    public static DeviceType GuessType(NeighborDto n)
    {
        var board = n.Board ?? "";
        var platform = n.Platform ?? "";
        var caps = (n.Capabilities ?? "").ToLowerInvariant();
        if (IsRouterOs(n))
        {
            if (board.StartsWith("CRS", StringComparison.OrdinalIgnoreCase) || board.StartsWith("CSS", StringComparison.OrdinalIgnoreCase))
                return DeviceType.Switch;
            if (board.StartsWith("cAP", StringComparison.OrdinalIgnoreCase) || board.StartsWith("wAP", StringComparison.OrdinalIgnoreCase)
                || board.StartsWith("Audience", StringComparison.OrdinalIgnoreCase))
                return DeviceType.AccessPoint;
            return DeviceType.Router;
        }
        if (platform.Contains("Phone", StringComparison.OrdinalIgnoreCase))
            return DeviceType.Phone;
        // Molti switch L2/L3 annunciano anche "router": il modello ("... Smart Switch") conta di più
        if (platform.Contains("switch", StringComparison.OrdinalIgnoreCase))
            return DeviceType.Switch;
        if (caps.Contains("telephone"))
            return DeviceType.Phone;
        if (caps.Contains("wlan-ap"))
            return DeviceType.AccessPoint;
        if (platform.Contains("WS-C", StringComparison.OrdinalIgnoreCase) || platform.Contains("Catalyst", StringComparison.OrdinalIgnoreCase)
            || (caps.Contains("switch") || caps.Contains("bridge")) && !caps.Contains("router"))
            return DeviceType.Switch;
        if (caps.Contains("router"))
            return DeviceType.Router;
        if (caps.Contains("station") || caps.Contains("host"))
            return DeviceType.Pc;
        return DeviceType.Other;
    }

    /// <summary>
    /// Tipo dal produttore del MAC, quando sysDescr, porte e nomi non bastano: solo marchi che fanno quasi soltanto quel
    /// tipo di apparato (HP, Cisco, Apple... restano "Altro"). Una VM VMware/Proxmox/Hyper-V è un server.
    /// </summary>
    public static DeviceType VendorType(string? vendor, IReadOnlyList<int> ports)
    {
        if (vendor is null || vendor == MacVendors.PrivateMac)
            return DeviceType.Other;
        bool Is(params string[] brands) => brands.Any(b => vendor.Contains(b, StringComparison.OrdinalIgnoreCase));

        if (Is("Hikvision", "Dahua", "Axis", "Uniview", "Hanwha", "Vivotek", "Reolink", "Mobotix")) return DeviceType.Camera;
        if (Is("Yealink", "Fanvil", "Snom", "Grandstream", "Polycom", "Gigaset", "Mitel", "Avaya", "Alcatel-Lucent Enterprise")) return DeviceType.Phone;
        if (Is("Kyocera", "Brother", "Ricoh", "Lexmark", "Xerox", "Konica", "Epson", "Seiko Epson", "Canon", "Sharp", "Utax", "Triumph")) return DeviceType.Printer;
        if (Is("Synology", "QNAP", "Western Digital", "Buffalo")) return DeviceType.Storage;
        if (Is("APC", "American Power Conversion", "Schneider", "Eaton", "Riello", "Vertiv")) return DeviceType.Ups;
        if (Is("VMware", "Proxmox", "Microsoft", "Xensource", "Nutanix", "Supermicro")) return DeviceType.Server;
        if (Is("MikroTik")) return DeviceType.Router;
        if (Is("Fortinet", "SonicWall", "WatchGuard", "Palo Alto", "Sophos", "Netgate")) return DeviceType.Firewall;
        if (Is("Ubiquiti", "Aruba", "Ruckus", "Cambium")) return DeviceType.AccessPoint;
        if (Is("ASRock", "ASUS", "Gigabyte", "MSI", "Intel", "Dell", "Lenovo") && ports.Contains(3389)) return DeviceType.Pc;
        return DeviceType.Other;
    }

    public static bool IsRouterOs(NeighborDto n) => n.Platform?.Contains("MikroTik", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Un cavo per coppia di interfacce: le letture dei due lati concordano se l'interfaccia di un lato coincide; un
    /// lato ignoto (vicino che non annuncia la porta) si unisce all'unico cavo noto.
    /// </summary>
    private static List<(string? A, string? B)> Cables(List<(string? A, string? B)> sides)
    {
        var cables = new List<(string? A, string? B)>();
        foreach (var (a, b) in sides.OrderByDescending(s => (s.A is not null ? 1 : 0) + (s.B is not null ? 1 : 0)))
        {
            var i = cables.FindIndex(c => (a is not null && Same(c.A, a)) || (b is not null && Same(c.B, b)));
            if (i < 0 && cables.Count == 1 && (a is null || cables[0].A is null) && (b is null || cables[0].B is null))
                i = 0;
            if (i >= 0)
                cables[i] = (cables[i].A ?? a, cables[i].B ?? b);
            else
                cables.Add((a, b));
        }
        return cables;
    }

    /// <summary>Interfaccia locale per nome: quella annunciata, altrimenti dall'ifIndex (LLDP/CDP) con l'inventario.</summary>
    private static string? LocalName(NeighborDto n, DeviceInfo seer, ILookup<Guid, InterfaceInfo> ifaces) =>
        n.LocalInterface ?? (n.LocalIfIndex is { } i ? ifaces[seer.Id].FirstOrDefault(x => x.IfIndex == i)?.Name : null);

    /// <summary>
    /// Sorgente di traffico: l'interfaccia del device nell'inventario SNMP, per nome. RouterOS in LLDP-MIB descrive le
    /// porte di un bridge come "bridge/ether2": vale anche il nome dopo la barra, se è un'interfaccia dell'inventario.
    /// </summary>
    private static InterfaceInfo? Source(DeviceInfo device, string? interfaceName, ILookup<Guid, InterfaceInfo> ifaces) =>
        !device.HasInterfaces ? null : FindInterface(ifaces[device.Id].ToList(), interfaceName, i => i.Name);

    /// <summary>
    /// Interfaccia dell'inventario per nome annunciato: esatto, oppure il nome dopo la barra e prima di un eventuale
    /// commento: RouterOS in LLDP-MIB descrive le porte di un bridge come "bridge/ether2" o "bridgeLan/ether6 pc-uffici".
    /// Usato anche da "Aggiungi" per la sorgente di traffico.
    /// </summary>
    public static T? FindInterface<T>(IReadOnlyList<T> inventory, string? announced, Func<T, string?> name) where T : class
    {
        if (announced is null)
            return null;
        var candidates = new List<string> { announced };
        if (announced.IndexOf('/') is var slash and > 0)
        {
            var port = announced[(slash + 1)..];
            candidates.Add(port);
            if (port.IndexOf(' ') is var space and > 0)
                candidates.Add(port[..space]);
        }
        return candidates.Select(c => inventory.FirstOrDefault(i => Same(name(i), c))).FirstOrDefault(i => i is not null);
    }

    private static string? IfName(Guid deviceId, int? ifIndex, ILookup<Guid, InterfaceInfo> ifaces) =>
        ifIndex is { } i ? ifaces[deviceId].FirstOrDefault(x => x.IfIndex == i)?.Name : null;

    /// <summary>Nome DNS completo ridotto all'host ("pc-uffici.dominio.local" → "pc-uffici"); gli altri nomi restano.</summary>
    private static string ShortName(string name) =>
        name.Count(c => c == '.') >= 2 && !System.Net.IPAddress.TryParse(name, out _) ? name[..name.IndexOf('.')] : name;

    private static string? FirstLine(string? s) => s?.Split('\n', '\r').FirstOrDefault(l => l.Trim().Length > 0)?.Trim();

    /// <summary>Modello dalla piattaforma ("S3300-52X ProSAFE 48-Port..." → "S3300-52X"), per i vicini senza nome.</summary>
    private static string? Model(string? platform) =>
        platform?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() is { Length: >= 3 } model
        && !GenericWords.Contains(model) ? model : null;

    /// <summary>Prime parole di sysDescr/piattaforma che non identificano l'apparato.</summary>
    private static readonly HashSet<string> GenericWords = new(StringComparer.OrdinalIgnoreCase)
        { "MikroTik", "RouterOS", "Linux", "Windows", "Hardware:", "Cisco", "HP", "Darwin", "FreeBSD", "VMware", "Software" };

    private static (Guid, Guid) Pair(Guid a, Guid b) => a.CompareTo(b) <= 0 ? (a, b) : (b, a);

    private static bool Same(string? a, string? b) => a is not null && b is not null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string Norm(string? s) => s?.ToLowerInvariant() ?? "";
}
