// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Globalization;
using System.Text;
using VedettaVip.Api.Data.Entities;
using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;

namespace VedettaVip.Api.Services;

public enum AlertKind { Down, Partial, Recovery, AgentOffline, AgentOnline, Threshold, ThresholdCleared }

/// <summary>Regole di notifica (pure, coperte da test).</summary>
public static class AlertRules
{
    /// <summary>
    /// Tipo di avviso di un evento; null se non si notifica (es. Unknown → Up alla prima rilevazione,
    /// Up → Unknown non viene mai generato).
    /// </summary>
    public static AlertKind? KindOf(string type, NodeState? from, NodeState? to) => type switch
    {
        EventTypes.AgentOffline => AlertKind.AgentOffline,
        EventTypes.AgentOnline => AlertKind.AgentOnline,
        EventTypes.ThresholdRaised => AlertKind.Threshold,
        EventTypes.ThresholdCleared => AlertKind.ThresholdCleared,
        EventTypes.StateChange => to switch
        {
            NodeState.Down => AlertKind.Down,
            NodeState.Partial => AlertKind.Partial,
            NodeState.Up when from is NodeState.Down or NodeState.Partial => AlertKind.Recovery,
            _ => null
        },
        _ => null
    };

    /// <summary>
    /// L'iscrizione riceve l'avviso? Down sempre; Partial con "Down e Partial" o "tutti i problemi"; soglie solo con
    /// "tutti i problemi"; ripristino/rientro solo se richiesto e se il problema era tra quelli notificati
    /// all'iscrizione; agente offline sempre, agente online come un ripristino.
    /// </summary>
    public static bool Qualifies(AlertFilter filter, bool notifyRecovery, AlertKind kind, NodeState? from) => kind switch
    {
        AlertKind.Down or AlertKind.AgentOffline => true,
        AlertKind.Partial => filter != AlertFilter.DownOnly,
        AlertKind.Threshold => filter == AlertFilter.AllProblems,
        AlertKind.Recovery => notifyRecovery && (from == NodeState.Down || filter != AlertFilter.DownOnly),
        AlertKind.ThresholdCleared => notifyRecovery && filter == AlertFilter.AllProblems,
        AlertKind.AgentOnline => notifyRecovery,
        _ => false
    };

    /// <summary>Problemi (aperti finché non rientrano): possono essere rimandati per manutenzione e avere promemoria.</summary>
    public static bool IsProblem(AlertKind kind) => kind is AlertKind.Down or AlertKind.Partial or AlertKind.Threshold or AlertKind.AgentOffline;

    /// <summary>Promemoria: Down e agenti offline; Partial e soglie solo se richiesto nelle impostazioni.</summary>
    public static bool RemindAbout(AlertKind kind, bool includeWarnings) =>
        kind is AlertKind.Down or AlertKind.AgentOffline || (includeWarnings && kind is AlertKind.Partial or AlertKind.Threshold);

    /// <summary>Backoff dei tentativi di invio falliti: 1, 2, 5, 10, 30, 60 minuti, poi ogni 2 ore.</summary>
    public static TimeSpan RetryDelay(int attempts) => attempts switch
    {
        <= 1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(2),
        3 => TimeSpan.FromMinutes(5),
        4 => TimeSpan.FromMinutes(10),
        5 => TimeSpan.FromMinutes(30),
        6 => TimeSpan.FromMinutes(60),
        _ => TimeSpan.FromHours(2)
    };

    public const int MaxAttempts = 8;
}

/// <summary>Dati di un avviso per comporre il messaggio.</summary>
public sealed record AlertContext(
    AlertKind Kind,
    string Subject,
    string? Address,
    string? Customer,
    IReadOnlyList<string> Maps,
    DateTimeOffset Time,
    TimeZoneInfo TimeZone,
    NodeState? FromState,
    TimeSpan? ProblemDuration,
    IReadOnlyList<string> UnreachableDependents,
    string? Link,
    /// <summary>Dettaglio dell'evento (per le soglie: valore, finestra e soglia).</summary>
    string? Detail = null,
    /// <summary>Promemoria di un problema ancora aperto: ProblemDuration = da quanto è aperto.</summary>
    bool IsReminder = false,
    /// <summary>Chiave dell'avviso (Event.AlertKey): distingue soglie, interfacce ("if:") e tunnel ("wg:").</summary>
    string? AlertKey = null);

/// <summary>Testo degli avvisi, uguale per email (oggetto + corpo) e Telegram (oggetto come prima riga).</summary>
public static class AlertMessage
{
    private const int MaxDependentsListed = 20;

    /// <summary>Le interfacce e i tunnel sorvegliati usano lo stesso motore delle soglie, ma l'oggetto deve dire cosa è successo.</summary>
    private static string ProblemLabel(string? alertKey) => alertKey switch
    {
        not null when alertKey.StartsWith("if:", StringComparison.Ordinal) => "INTERFACCIA GIÙ",
        not null when alertKey.StartsWith("wg:", StringComparison.Ordinal) => "TUNNEL GIÙ",
        _ => "SOGLIA"
    };

    private static string RecoveryLabel(string? alertKey) => alertKey switch
    {
        not null when alertKey.StartsWith("if:", StringComparison.Ordinal) => "INTERFACCIA RIPRISTINATA",
        not null when alertKey.StartsWith("wg:", StringComparison.Ordinal) => "TUNNEL RIPRISTINATO",
        _ => "RIENTRATO"
    };

    public static (string Subject, string Body) Build(AlertContext c)
    {
        var who = c.Address is null ? c.Subject : $"{c.Subject} ({c.Address})";
        var since = TimeZoneInfo.ConvertTime(c.Time, c.TimeZone).ToString("HH:mm:ss 'del' dd/MM/yyyy", CultureInfo.InvariantCulture);

        var (subject, headline) = c.Kind switch
        {
            AlertKind.Down => ($"[VedettaVip] DOWN: {who}", $"{who} non risponde al ping."),
            AlertKind.Partial => ($"[VedettaVip] PARZIALE: {who}", $"{who} risponde al ping ma non a SNMP."),
            AlertKind.Recovery => ($"[VedettaVip] RIPRISTINO: {who}{(c.ProblemDuration is { } d ? $" dopo {Duration(d)}" : "")}",
                $"{who} è di nuovo raggiungibile (era {StateName(c.FromState)}{(c.ProblemDuration is { } d2 ? $" da {Duration(d2)}" : "")})."),
            AlertKind.AgentOffline => ($"[VedettaVip] AGENTE OFFLINE: {c.Subject}",
                $"L'agente di polling {c.Subject} non invia dati: gli stati dei suoi dispositivi non sono aggiornati."),
            AlertKind.Threshold => ($"[VedettaVip] {ProblemLabel(c.AlertKey)}: {who}", DetailAfterName(c.Detail) ?? $"{who}: soglia superata."),
            AlertKind.ThresholdCleared => ($"[VedettaVip] {RecoveryLabel(c.AlertKey)}: {who}", DetailAfterName(c.Detail) ?? $"{who}: valore rientrato sotto la soglia."),
            _ => ($"[VedettaVip] AGENTE ONLINE: {c.Subject}", $"L'agente di polling {c.Subject} ha ripreso a inviare dati.")
        };

        if (c.IsReminder)
        {
            var open = c.ProblemDuration is { } p ? $" da {Duration(p)}" : "";
            subject = subject.Replace("[VedettaVip] ", "[VedettaVip] PROMEMORIA ");
            headline = $"Problema ancora aperto{open} e non preso in carico. {headline}";
        }

        var body = new StringBuilder();
        body.AppendLine(headline);
        body.AppendLine();
        body.AppendLine($"Rilevato alle {since}");
        if (c.Customer is not null)
            body.AppendLine($"Cliente: {c.Customer}");
        if (c.Maps.Count > 0)
            body.AppendLine($"Mappe: {string.Join(", ", c.Maps)}");

        if (c.UnreachableDependents.Count > 0)
        {
            var listed = c.UnreachableDependents.Take(MaxDependentsListed);
            var more = c.UnreachableDependents.Count - MaxDependentsListed;
            body.AppendLine($"Dispositivi dipendenti non raggiungibili ({c.UnreachableDependents.Count}): " +
                            string.Join(", ", listed) + (more > 0 ? $" e altri {more}" : ""));
        }

        if (c.Link is not null)
        {
            body.AppendLine();
            body.AppendLine(c.Link);
        }

        return (subject, body.ToString().TrimEnd());
    }

    /// <summary>Il messaggio degli eventi di soglia inizia con "Nome (indirizzo): ": il resto è il dettaglio.</summary>
    private static string? DetailAfterName(string? detail) =>
        detail is null ? null : detail.IndexOf("): ", StringComparison.Ordinal) is var i and >= 0 ? Capitalize(detail[(i + 3)..]) + "." : detail;

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>"45 s", "12 min", "3 h 5 min", "2 g 4 h".</summary>
    public static string Duration(TimeSpan d) => d switch
    {
        { TotalMinutes: < 1 } => $"{Math.Max(0, (int)d.TotalSeconds)} s",
        { TotalHours: < 1 } => $"{(int)d.TotalMinutes} min",
        { TotalDays: < 1 } => d.Minutes == 0 ? $"{(int)d.TotalHours} h" : $"{(int)d.TotalHours} h {d.Minutes} min",
        _ => d.Hours == 0 ? $"{(int)d.TotalDays} g" : $"{(int)d.TotalDays} g {d.Hours} h"
    };

    private static string StateName(NodeState? s) => s switch
    {
        NodeState.Down => "Down",
        NodeState.Partial => "Partial",
        _ => "irraggiungibile"
    };
}
