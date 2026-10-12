// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Data.Entities;

/// <summary>
/// Interruzione di un servizio di un device (storico dei down, come la scheda Outages di The Dude). Aperta e chiusa
/// da <see cref="Services.OutageBook"/> negli stessi punti in cui nascono gli eventi, ma indipendente dalla presa in
/// carico. Key identifica cosa è giù sul device ("ping", "snmp", "if:ether1", "link:{id}"...): al massimo
/// un'interruzione aperta per (DeviceId, Key). Conservata finché non la si rimuove a mano ("Rimuovi risolte").
/// </summary>
public sealed class Outage
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }
    public OutageService Service { get; set; }
    public required string Key { get; set; }
    /// <summary>Descrizione per l'elenco (es. "interfaccia ether1 giù (non running)", "latenza media 150 ms, soglia 100 ms").</summary>
    public string? Detail { get; set; }
    /// <summary>Inizio, con l'orologio dell'agente per ping/SNMP/RouterOS, dell'API per le soglie (UTC).</summary>
    public DateTimeOffset StartedAt { get; set; }
    /// <summary>Fine; null = in corso.</summary>
    public DateTimeOffset? EndedAt { get; set; }
    /// <summary>Perché si è chiusa, se non per un ripristino (es. "monitoraggio disattivato").</summary>
    public string? EndNote { get; set; }
    /// <summary>Finestra di manutenzione attiva all'apertura o alla chiusura (nome); null = fuori manutenzione.</summary>
    public string? Maintenance { get; set; }
}
