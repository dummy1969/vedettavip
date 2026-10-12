// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;

namespace VedettaVip.Api.Services;

/// <summary>
/// CSV per gli export: separatore ; (si apre direttamente in Excel in italiano), CRLF, BOM UTF-8 (Excel riconosce
/// l'UTF-8). Coperto da test.
/// </summary>
public static class CsvWriter
{
    /// <summary>
    /// Testo CSV. Con <paramref name="protectFormulas"/> le celle che iniziano con = + - @ (o tab/CR) prendono un apice
    /// davanti: Excel le eseguirebbe come formule (CSV injection), e nomi e messaggi arrivano da dispositivi e utenti.
    /// L'export dei dispositivi non lo usa: deve rileggersi identico con l'import.
    /// </summary>
    public static string Write(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows, bool protectFormulas = false)
    {
        var sb = new StringBuilder();
        sb.AppendJoin(';', header.Select(h => Quote(h, false))).Append("\r\n");
        foreach (var row in rows)
            sb.AppendJoin(';', row.Select(v => Quote(v, protectFormulas))).Append("\r\n");
        return sb.ToString();
    }

    /// <summary>Risposta da scaricare con il nome indicato.</summary>
    public static FileContentHttpResult File(string csv, string fileName) =>
        TypedResults.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv; charset=utf-8", fileName);

    /// <summary>Istante nel fuso indicato, ordinabile anche come testo ("2026-10-12 14:03:27").</summary>
    public static string Time(DateTimeOffset? at, TimeZoneInfo zone) =>
        at is { } t ? TimeZoneInfo.ConvertTime(t, zone).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : "";

    private static string Quote(string value, bool protectFormulas)
    {
        if (protectFormulas && value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = "'" + value;
        return value.IndexOfAny([';', ',', '"', '\n', '\r']) >= 0 || value != value.Trim()
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }
}
