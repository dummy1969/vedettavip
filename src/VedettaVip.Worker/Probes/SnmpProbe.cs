// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Collections.Concurrent;
using System.Net;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;

namespace VedettaVip.Worker.Probes;

/// <summary>Contatori di un'interfaccia letti in un GET, con il sysUpTime della stessa risposta (base dei tempi).</summary>
/// <param name="Is32Bit">Contatori ifInOctets/ifOutOctets (ifTable) invece di ifHC* (ifXTable): si azzerano a 2³² byte.</param>
public sealed record SnmpInterfaceCounters(
    int IfIndex, uint UptimeTicks, ulong InOctets, ulong OutOctets, long? SpeedBps, string? Name, bool Is32Bit = false);

/// <param name="Ok">Il device ha risposto al GET di sysUpTime (decide lo stato Partial).</param>
public sealed record SnmpPollResult(bool Ok, IReadOnlyList<SnmpInterfaceCounters> Interfaces)
{
    public static readonly SnmpPollResult Failed = new(false, []);
}

/// <summary>
/// Poll SNMP di un device: sysUpTime (verifica SNMP) e, solo in v2c, i contatori a 64 bit delle interfacce
/// richieste (ifHCInOctets, ifHCOutOctets, ifHighSpeed, ifName), nello stesso GET per non aggiungere round-trip.
/// Gli apparati senza ifXTable (es. DrayTek Vigor con firmware datati, solo MIB-II) non hanno i contatori a 64 bit:
/// per quell'indirizzo si passa ai contatori a 32 bit di ifTable (ifInOctets, ifOutOctets, ifSpeed, ifDescr) e ogni
/// <see cref="LegacyRetry"/> si riprova ifXTable (aggiornamento del firmware).
/// </summary>
public sealed class SnmpProbe(ILogger<SnmpProbe> logger, TimeProvider time)
{
    public static readonly TimeSpan LegacyRetry = TimeSpan.FromHours(1);

    /// <summary>Indirizzi senza contatori a 64 bit, con l'istante in cui riprovarli.</summary>
    private readonly ConcurrentDictionary<IPAddress, DateTimeOffset> legacyUntil = new();

    // Interfacce per GET: 1 + 4×8 = 33 varbind, la risposta resta sotto la MTU
    private const int InterfacesPerRequest = 8;

    private static readonly ObjectIdentifier SysUpTime = new("1.3.6.1.2.1.1.3.0");
    private const string IfName = "1.3.6.1.2.1.31.1.1.1.1";
    private const string IfHcInOctets = "1.3.6.1.2.1.31.1.1.1.6";
    private const string IfHcOutOctets = "1.3.6.1.2.1.31.1.1.1.10";
    private const string IfHighSpeed = "1.3.6.1.2.1.31.1.1.1.15";
    // MIB-II ifTable: contatori a 32 bit, velocità in bps (max 4.294.967.295), descrizione
    private const string IfDescr = "1.3.6.1.2.1.2.2.1.2";
    private const string IfSpeed = "1.3.6.1.2.1.2.2.1.5";
    private const string IfInOctets = "1.3.6.1.2.1.2.2.1.10";
    private const string IfOutOctets = "1.3.6.1.2.1.2.2.1.16";

    /// <param name="ifIndexes">Interfacce da misurare; ignorate in v1 (i Counter64 richiedono v2c).</param>
    public async Task<SnmpPollResult> PollAsync(
        IPAddress address, VersionCode version, string community, IReadOnlyList<int> ifIndexes, TimeSpan timeout, CancellationToken ct)
    {
        List<int[]> chunks = version == VersionCode.V1 || ifIndexes.Count == 0
            ? [[]]
            : ifIndexes.Chunk(InterfacesPerRequest).ToList();

        var legacy = legacyUntil.TryGetValue(address, out var until) && time.GetUtcNow() < until;
        var interfaces = new List<SnmpInterfaceCounters>();
        for (var i = 0; i < chunks.Count; i++)
        {
            var response = await GetAsync(address, version, community, BuildRequest(chunks[i], legacy), timeout, ct);
            if (response is null || response.Count == 0 || response[0].Data is not TimeTicks uptime)
            {
                // Solo il primo GET decide se SNMP risponde. Se conteneva interfacce, verifica con il solo sysUpTime:
                // un IfIndex sbagliato o un agent che rifiuta l'intera richiesta non deve rendere il device Partial.
                if (i == 0 && (chunks[0].Length == 0 || !await RespondsAsync(address, version, community, timeout, ct)))
                    return SnmpPollResult.Failed;
                continue;
            }

            interfaces.AddRange(ParseInterfaces(chunks[i], response, uptime.ToUInt32(), legacy));
        }

        // Nessun contatore a 64 bit (noSuchObject): dal prossimo poll i contatori a 32 bit di ifTable
        if (!legacy && interfaces.Count == 0 && ifIndexes.Count > 0 && version != VersionCode.V1)
        {
            legacyUntil[address] = time.GetUtcNow() + LegacyRetry;
            logger.LogInformation("{Address}: niente contatori a 64 bit (ifXTable), uso quelli a 32 bit di ifTable", address);
        }
        else if (legacy && interfaces.Count == 0)
        {
            legacyUntil.TryRemove(address, out _); // nemmeno i 32 bit: si ricomincia dai 64 al prossimo poll
        }

        return new SnmpPollResult(true, interfaces);
    }

    /// <summary>
    /// Walk di una colonna di tabella (es. ifName) con GetBulk (solo v2c). Restituisce valore per indice
    /// (ultimo componente dell'OID); null se il device non risponde. Implementato qui invece di Messenger.BulkWalk
    /// per usare request-id positivi (vedi <see cref="NextRequestId"/>).
    /// </summary>
    public async Task<Dictionary<int, ISnmpData>?> WalkColumnAsync(
        IPAddress address, string community, string columnOid, TimeSpan timeout, CancellationToken ct)
    {
        var rows = await WalkSubtreeAsync(address, community, columnOid, timeout, ct);
        if (rows is null)
            return null;
        var result = new Dictionary<int, ISnmpData>();
        foreach (var (index, data) in rows)
            result[(int)index[^1]] = data;
        return result;
    }

    /// <summary>
    /// Walk GetBulk di un sottoalbero (solo v2c) con l'indice completo dopo l'OID (es. "timeMark.porta.indice" della
    /// LLDP-MIB). Null se il device non risponde; a metà walk si tiene quanto letto.
    /// </summary>
    public async Task<List<(uint[] Index, ISnmpData Data)>?> WalkSubtreeAsync(
        IPAddress address, string community, string rootOid, TimeSpan timeout, CancellationToken ct)
    {
        const int maxRepetitions = 20;
        const int maxRequests = 500; // 10.000 righe: oltre c'è un agent che non avanza
        var prefix = rootOid + ".";
        var rootLength = new ObjectIdentifier(rootOid).ToNumerical().Length;
        var result = new List<(uint[], ISnmpData)>();
        var next = new ObjectIdentifier(rootOid);

        for (var request = 0; request < maxRequests; request++)
        {
            var variables = await SendAsync(address, timeout, ct,
                id => new GetBulkRequestMessage(id, VersionCode.V2, new OctetString(community), 0, maxRepetitions, [new Variable(next)]));
            if (variables is null)
                return request == 0 ? null : result;

            foreach (var v in variables)
            {
                if (v.Data.TypeCode is SnmpType.EndOfMibView or SnmpType.NoSuchObject or SnmpType.NoSuchInstance
                    || !v.Id.ToString().StartsWith(prefix, StringComparison.Ordinal))
                    return result;

                result.Add((v.Id.ToNumerical()[rootLength..], v.Data));
                next = v.Id;
            }

            if (variables.Count == 0)
                return result;
        }

        logger.LogWarning("Walk di {Root} su {Address} interrotto dopo {Max} richieste", rootOid, address, maxRequests);
        return result;
    }

    private static readonly ObjectIdentifier SysDescr = new("1.3.6.1.2.1.1.1.0");
    private static readonly ObjectIdentifier SysName = new("1.3.6.1.2.1.1.5.0");

    /// <summary>sysName e sysDescr (v2c) per la scansione di subnet; null se la community non risponde.</summary>
    public async Task<(string? SysName, string? SysDescr)?> GetSystemAsync(IPAddress address, string community, TimeSpan timeout, CancellationToken ct)
    {
        var response = await GetAsync(address, VersionCode.V2, community, [new Variable(SysName), new Variable(SysDescr)], timeout, ct);
        if (response is not { Count: 2 })
            return null;
        static string? Text(ISnmpData d) => d is OctetString s && s.ToString().Trim() is { Length: > 0 } t ? t : null;
        return (Text(response[0].Data), Text(response[1].Data));
    }

    private async Task<bool> RespondsAsync(IPAddress address, VersionCode version, string community, TimeSpan timeout, CancellationToken ct) =>
        await GetAsync(address, version, community, BuildRequest([], false), timeout, ct) is [{ Data: TimeTicks }, ..];

    /// <summary>sysUpTime + 4 varbind per interfaccia: contatori in/out, velocità, nome (ifXTable o, in legacy, ifTable).</summary>
    private static List<Variable> BuildRequest(int[] ifIndexes, bool legacy)
    {
        var columns = legacy ? new[] { IfInOctets, IfOutOctets, IfSpeed, IfDescr } : [IfHcInOctets, IfHcOutOctets, IfHighSpeed, IfName];
        var variables = new List<Variable>(1 + ifIndexes.Length * 4) { new(SysUpTime) };
        foreach (var ifIndex in ifIndexes)
            foreach (var column in columns)
                variables.Add(new Variable(new ObjectIdentifier($"{column}.{ifIndex}")));
        return variables;
    }

    /// <summary>
    /// Risposta nello stesso ordine della richiesta. Un'interfaccia inesistente risponde noSuchInstance
    /// (v2c): viene saltata, il chiamante la vedrà come "n/d".
    /// </summary>
    private IEnumerable<SnmpInterfaceCounters> ParseInterfaces(int[] ifIndexes, IList<Variable> response, uint uptime, bool legacy)
    {
        for (var i = 0; i < ifIndexes.Length; i++)
        {
            var at = 1 + i * 4;
            if (response.Count < at + 4)
                yield break;

            var name = response[at + 3].Data is OctetString s && s.ToString() is { Length: > 0 } text ? text : null;
            if (legacy)
            {
                if (response[at].Data is not Counter32 in32 || response[at + 1].Data is not Counter32 out32)
                    continue;
                // ifSpeed in bps; 4.294.967.295 = "oltre 4,29 Gbps" (valore vero in ifHighSpeed, che qui non c'è)
                long? bps = response[at + 2].Data is Gauge32 g && g.ToUInt32() is > 0 and < uint.MaxValue ? g.ToUInt32() : null;
                yield return new SnmpInterfaceCounters(ifIndexes[i], uptime, in32.ToUInt32(), out32.ToUInt32(), bps, name, Is32Bit: true);
                continue;
            }

            if (response[at].Data is not Counter64 inOctets || response[at + 1].Data is not Counter64 outOctets)
            {
                logger.LogDebug("IfIndex {IfIndex}: contatori ifHC* non disponibili ({Type})", ifIndexes[i], response[at].Data.TypeCode);
                continue;
            }

            long? speed = response[at + 2].Data is Gauge32 mbps && mbps.ToUInt32() > 0 ? mbps.ToUInt32() * 1_000_000L : null;
            yield return new SnmpInterfaceCounters(ifIndexes[i], uptime, inOctets.ToUInt64(), outOctets.ToUInt64(), speed, name);
        }
    }

    /// <summary>
    /// Request-id sempre positivo. Messenger.GetAsync usa un contatore che parte da un valore casuale e può essere
    /// negativo: alcuni agent (es. RouterOS) rimandano l'id negativo codificato su 5 byte con estensione del segno
    /// (BER non minimale) e SharpSnmpLib scarta la risposta ("Truncation error for 32-bit integer coding").
    /// Con un processo avviato su id negativi, ogni GET verso quei device fallirebbe (device sempre Partial).
    /// </summary>
    public static int NextRequestId() => Random.Shared.Next(1, int.MaxValue);

    private Task<IList<Variable>?> GetAsync(
        IPAddress address, VersionCode version, string community, List<Variable> variables, TimeSpan timeout, CancellationToken ct) =>
        SendAsync(address, timeout, ct, id => new GetRequestMessage(id, version, new OctetString(community), variables));

    /// <summary>Invia la richiesta costruita con un request-id positivo; null se timeout, errore di rete o errore SNMP.</summary>
    private async Task<IList<Variable>?> SendAsync(
        IPAddress address, TimeSpan timeout, CancellationToken ct, Func<int, ISnmpMessage> build)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            var request = build(NextRequestId());
            var response = await request.GetResponseAsync(new IPEndPoint(address, 161), cts.Token);
            var pdu = response.Pdu();
            if (pdu.ErrorStatus.ToInt32() != 0)
            {
                // Es. noSuchName in v1 o tooBig: per il chiamante è un GET fallito
                logger.LogDebug("SNMP verso {Address}: errore {Status} sul varbind {Index}", address, pdu.ErrorStatus, pdu.ErrorIndex);
                return null;
            }
            return pdu.Variables;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null; // timeout
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Porta chiusa (ICMP port unreachable), errore SNMP (es. noSuchName in v1), risposta malformata
            logger.LogDebug("SNMP verso {Address} fallito: {Message} ({Cause})", address, ex.Message, ex.GetBaseException().Message);
            return null;
        }
    }
}
