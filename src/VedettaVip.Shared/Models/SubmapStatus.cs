// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Shared.Contracts;

namespace VedettaVip.Shared.Models;

/// <summary>Conteggio degli stati dei dispositivi contenuti in una sottomappa (e nelle sue sottomappe).</summary>
public sealed record SubmapCounts(int Up, int Partial, int Down, int Unknown, int Maintenance)
{
    public int Total => Up + Partial + Down + Unknown + Maintenance;

    /// <summary>Testo breve per l'etichetta: "2 down · 1 partial su 12", "12 up", "nessun dispositivo".</summary>
    public string Summary()
    {
        if (Total == 0)
            return "nessun dispositivo";
        var problems = new List<string>();
        if (Down > 0) problems.Add($"{Down} down");
        if (Partial > 0) problems.Add($"{Partial} partial");
        if (problems.Count > 0)
            return $"{string.Join(" · ", problems)} su {Total}";
        return Unknown > 0 && Up == 0 ? $"{Total} sconosciuti" : $"{Up} up" + (Unknown > 0 ? $" · {Unknown} sconosciuti" : "");
    }

    /// <summary>Descrizione completa per il tooltip.</summary>
    public string Details() =>
        $"{Total} dispositivi: {Down} down, {Partial} partial, {Up} up, {Unknown} sconosciuti" +
        (Maintenance > 0 ? $", {Maintenance} in manutenzione (esclusi dal colore)" : "");
}

/// <summary>
/// Stato aggregato di un nodo-sottomappa: il peggiore tra i dispositivi contenuti (Down &gt; Partial &gt; Up).
/// Unknown solo se nessun dispositivo ha uno stato noto: un device disabilitato o senza dati non rende grigia
/// un'intera sede. I dispositivi in manutenzione non contano nel colore (il problema è atteso) ma sono contati a parte.
/// </summary>
public static class SubmapStatus
{
    public static SubmapCounts Count(IEnumerable<SubmapMemberDto> members)
    {
        int up = 0, partial = 0, down = 0, unknown = 0, maintenance = 0;
        foreach (var m in members)
        {
            if (m.InMaintenance) { maintenance++; continue; }
            switch (m.State)
            {
                case NodeState.Up: up++; break;
                case NodeState.Partial: partial++; break;
                case NodeState.Down: down++; break;
                default: unknown++; break;
            }
        }
        return new SubmapCounts(up, partial, down, unknown, maintenance);
    }

    public static NodeState Aggregate(SubmapCounts c) =>
        c.Down > 0 ? NodeState.Down
        : c.Partial > 0 ? NodeState.Partial
        : c.Up > 0 ? NodeState.Up
        : NodeState.Unknown;
}
