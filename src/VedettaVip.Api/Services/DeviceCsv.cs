// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Text;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Services;

/// <summary>
/// Lettura e scrittura del CSV dei dispositivi. Separatore riconosciuto dall'intestazione (; , o tab: Excel in italiano
/// usa il punto e virgola), campi tra virgolette con "" per le virgolette, righe vuote o che iniziano con # ignorate.
/// Nomi di colonna in italiano o in inglese, senza distinzione di maiuscole, spazi, accenti o trattini.
/// </summary>
public static class DeviceCsv
{
    public const string Name = "name", Address = "address", Type = "type", Snmp = "snmp", Customer = "customer",
        Parent = "parent", SnmpProfile = "snmp_profile", Enabled = "enabled", Map = "map";

    /// <summary>Ordine delle colonne nell'export (e nel modello).</summary>
    public static readonly string[] Columns = [Name, Address, Type, Snmp, Customer, Parent, SnmpProfile, Enabled, Map];

    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["name"] = Name, ["nome"] = Name, ["dispositivo"] = Name, ["device"] = Name,
        ["address"] = Address, ["indirizzo"] = Address, ["ip"] = Address, ["ipaddress"] = Address, ["indirizzoip"] = Address,
        ["host"] = Address, ["hostname"] = Address,
        ["type"] = Type, ["tipo"] = Type,
        ["snmp"] = Snmp, ["snmpversion"] = Snmp, ["versionesnmp"] = Snmp,
        ["customer"] = Customer, ["cliente"] = Customer,
        ["parent"] = Parent, ["padre"] = Parent, ["dipendeda"] = Parent,
        ["snmpprofile"] = SnmpProfile, ["profilosnmp"] = SnmpProfile, ["profilo"] = SnmpProfile, ["snmpcredential"] = SnmpProfile,
        ["enabled"] = Enabled, ["abilitato"] = Enabled, ["attivo"] = Enabled,
        ["map"] = Map, ["mappa"] = Map, ["maps"] = Map, ["mappe"] = Map
    };

    public sealed record Row(int Line, IReadOnlyDictionary<string, string> Values)
    {
        public string Get(string column) => Values.GetValueOrDefault(column, "");
        public bool Has(string column) => Values.ContainsKey(column);
    }

    public sealed record Table(IReadOnlyList<string> Columns, IReadOnlyList<string> Ignored, IReadOnlyList<Row> Rows);

    /// <summary>Colonna canonica dal nome dell'intestazione; null se sconosciuta.</summary>
    public static string? Canonical(string header)
    {
        var normalized = new StringBuilder();
        foreach (var c in header.Normalize(NormalizationForm.FormD))
            if (char.IsLetterOrDigit(c))
                normalized.Append(char.ToLowerInvariant(c)); // accenti (segni combinanti), spazi, _ - . scartati
        return Aliases.GetValueOrDefault(normalized.ToString());
    }

    /// <exception cref="FormatException">Intestazione assente, colonne obbligatorie mancanti o duplicate.</exception>
    public static Table Parse(string text)
    {
        var records = ReadRecords(text.TrimStart('﻿'));
        var headerIndex = records.FindIndex(r => !IsBlankOrComment(r.Fields));
        if (headerIndex < 0)
            throw new FormatException("Il file è vuoto.");

        var header = records[headerIndex].Fields;
        var columns = new string?[header.Count];
        var known = new List<string>();
        var ignored = new List<string>();
        for (var i = 0; i < header.Count; i++)
        {
            var canonical = Canonical(header[i]);
            if (canonical is null)
            {
                if (header[i].Trim().Length > 0) ignored.Add(header[i].Trim());
                continue;
            }
            if (known.Contains(canonical))
                throw new FormatException($"Colonna \"{canonical}\" ripetuta nell'intestazione.");
            columns[i] = canonical;
            known.Add(canonical);
        }
        if (!known.Contains(Name) || !known.Contains(Address))
            throw new FormatException("L'intestazione deve contenere almeno le colonne nome e indirizzo " +
                                      "(es. \"nome;indirizzo;tipo;snmp;cliente\"). Trovate: " + string.Join(", ", header));

        var rows = new List<Row>();
        foreach (var (line, fields) in records.Skip(headerIndex + 1))
        {
            if (IsBlankOrComment(fields))
                continue;
            var values = new Dictionary<string, string>();
            for (var i = 0; i < columns.Length; i++)
                if (columns[i] is { } column)
                    values[column] = i < fields.Count ? fields[i].Trim() : "";
            rows.Add(new Row(line, values));
        }
        return new Table(known, ignored, rows);
    }

    private static bool IsBlankOrComment(List<string> fields) =>
        fields.All(f => f.Trim().Length == 0) || fields[0].TrimStart().StartsWith('#');

    /// <summary>Record CSV con il numero della riga di inizio; i campi tra virgolette possono contenere a capo.</summary>
    private static List<(int Line, List<string> Fields)> ReadRecords(string text)
    {
        var delimiter = DetectDelimiter(text);
        var records = new List<(int, List<string>)>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var line = 1;
        var recordLine = 1;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else
                {
                    if (c == '\n') line++;
                    field.Append(c);
                }
            }
            else if (c == '"' && field.ToString().Trim().Length == 0)
            {
                field.Clear();
                inQuotes = true;
            }
            else if (c == delimiter)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                fields.Add(field.ToString());
                records.Add((recordLine, fields));
                fields = [];
                field.Clear();
                recordLine = ++line;
            }
            else
            {
                field.Append(c);
            }
        }
        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            records.Add((recordLine, fields));
        }
        return records;
    }

    /// <summary>Il separatore più frequente (fuori dalle virgolette) nella prima riga non vuota.</summary>
    private static char DetectDelimiter(string text)
    {
        var counts = new Dictionary<char, int> { [';'] = 0, [','] = 0, ['\t'] = 0 };
        var inQuotes = false;
        var started = false;
        foreach (var c in text)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (!inQuotes && c is '\n' or '\r') { if (started) break; }
            else
            {
                started |= !char.IsWhiteSpace(c);
                if (!inQuotes && counts.ContainsKey(c)) counts[c]++;
            }
        }
        var best = counts.MaxBy(kv => kv.Value);
        return best.Value > 0 ? best.Key : ';';
    }

    // ---------- Valori ----------

    public static bool TryParseType(string s, out DeviceType type)
    {
        type = Key(s) switch
        {
            "" or "other" or "altro" => DeviceType.Other,
            "router" => DeviceType.Router,
            "switch" => DeviceType.Switch,
            "accesspoint" or "ap" or "wifi" => DeviceType.AccessPoint,
            "server" => DeviceType.Server,
            "firewall" or "fw" => DeviceType.Firewall,
            "storage" or "nas" or "san" => DeviceType.Storage,
            "pc" or "workstation" or "desktop" or "computer" => DeviceType.Pc,
            "printer" or "stampante" => DeviceType.Printer,
            "camera" or "telecamera" or "cctv" or "ipcam" => DeviceType.Camera,
            "phone" or "telefono" or "voip" => DeviceType.Phone,
            "ups" => DeviceType.Ups,
            _ => (DeviceType)(-1)
        };
        return Enum.IsDefined(type);
    }

    public static bool TryParseSnmp(string s, out SnmpVersion version)
    {
        version = Key(s) switch
        {
            "" or "none" or "nessuno" or "no" => SnmpVersion.None,
            "v1" or "1" => SnmpVersion.V1,
            "v2c" or "v2" or "2c" or "2" => SnmpVersion.V2c,
            "v3" or "3" => SnmpVersion.V3,
            _ => (SnmpVersion)(-1)
        };
        return Enum.IsDefined(version);
    }

    public static bool TryParseBool(string s, out bool value)
    {
        switch (Key(s))
        {
            case "" or "true" or "si" or "yes" or "1" or "x": value = true; return true;
            case "false" or "no" or "0": value = false; return true;
            default: value = false; return false;
        }
    }

    private static string Key(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.Normalize(NormalizationForm.FormD))
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    // ---------- Scrittura ----------

    public static string TypeText(DeviceType t) => t switch
    {
        DeviceType.Router => "router",
        DeviceType.Switch => "switch",
        DeviceType.AccessPoint => "accesspoint",
        DeviceType.Server => "server",
        DeviceType.Firewall => "firewall",
        DeviceType.Storage => "storage",
        DeviceType.Pc => "pc",
        DeviceType.Printer => "printer",
        DeviceType.Camera => "camera",
        DeviceType.Phone => "phone",
        DeviceType.Ups => "ups",
        _ => "other"
    };

    public static string SnmpText(SnmpVersion v) => v switch
    {
        SnmpVersion.V1 => "v1",
        SnmpVersion.V2c => "v2c",
        SnmpVersion.V3 => "v3",
        _ => "none"
    };

    /// <summary>CSV con ; (si apre direttamente in Excel in italiano), BOM UTF-8 aggiunto dall'endpoint.</summary>
    public static string Write(IEnumerable<IReadOnlyList<string>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendJoin(';', Columns).Append("\r\n");
        foreach (var row in rows)
            sb.AppendJoin(';', row.Select(Quote)).Append("\r\n");
        return sb.ToString();
    }

    private static string Quote(string value) =>
        value.IndexOfAny([';', ',', '"', '\n', '\r']) >= 0 || value != value.Trim()
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
}
