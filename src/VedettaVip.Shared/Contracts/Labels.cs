// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

namespace VedettaVip.Shared.Contracts;

/// <summary>Testi in italiano condivisi tra le pagine e gli export CSV, così le due viste usano le stesse parole.</summary>
public static class Labels
{
    public static string Severity(EventSeverity s) => s switch
    {
        EventSeverity.Critical => "critico",
        EventSeverity.Error => "errore",
        EventSeverity.Warning => "avviso",
        _ => "info"
    };

    public static string Service(OutageService s) => s switch
    {
        OutageService.Ping => "Ping",
        OutageService.Snmp => "SNMP",
        OutageService.Interface => "Interfaccia",
        OutageService.WireGuard => "WireGuard",
        OutageService.Latency => "Latenza",
        OutageService.Loss => "Perdita",
        OutageService.Cpu => "CPU",
        OutageService.Temperature => "Temperatura",
        OutageService.Link => "Utilizzo link",
        _ => s.ToString()
    };

    public static string NotifyState(NotifyState s) => s switch
    {
        Contracts.NotifyState.Pending => "in attesa",
        Contracts.NotifyState.Done => "elaborata", // esito degli invii nelle colonne dei conteggi
        Contracts.NotifyState.Suppressed => "soppressa",
        Contracts.NotifyState.Maintenance => "rimandata (manutenzione)",
        _ => "nessuna"
    };

    /// <summary>Durata come in The Dude: "00:03:20", "10g 12:04:38".</summary>
    public static string Duration(TimeSpan d)
    {
        if (d < TimeSpan.Zero)
            d = TimeSpan.Zero;
        var clock = $"{d.Hours:00}:{d.Minutes:00}:{d.Seconds:00}";
        return d.TotalDays >= 1 ? $"{(int)d.TotalDays}g {clock}" : clock;
    }
}
