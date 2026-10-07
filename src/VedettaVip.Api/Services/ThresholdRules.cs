// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

namespace VedettaVip.Api.Services;

public enum ThresholdKind { Latency, Loss, LinkUtilization, RouterOsCpu, RouterOsTemperature }

public enum ThresholdTransition { None, Raise, Clear }

/// <summary>Regole delle soglie sulle metriche (pure, coperte da test).</summary>
public static class ThresholdRules
{
    /// <summary>Isteresi: l'avviso aperto si chiude solo quando il valore scende sotto questa frazione della soglia.</summary>
    public const double ClearRatio = 0.8;

    /// <summary>
    /// Transizione dell'avviso: si apre se il valore supera la soglia, si chiude se scende sotto l'80% della soglia
    /// (o se la soglia è stata disattivata). Tra 80% e 100% resta com'è: niente oscillazioni.
    /// </summary>
    public static ThresholdTransition Evaluate(bool open, double value, double? threshold) =>
        Evaluate(open, value, threshold, threshold * ClearRatio);

    /// <summary>Come sopra, con il livello di chiusura esplicito (<see cref="ClearLevel"/>).</summary>
    public static ThresholdTransition Evaluate(bool open, double value, double? threshold, double? clearBelow) => (open, threshold) switch
    {
        (true, null or <= 0) => ThresholdTransition.Clear,
        (false, null or <= 0) => ThresholdTransition.None,
        (false, { } t) when value > t => ThresholdTransition.Raise,
        (true, { }) when value < clearBelow => ThresholdTransition.Clear,
        _ => ThresholdTransition.None
    };

    /// <summary>Gradi sotto la soglia di temperatura per chiudere l'avviso (l'80% di 75 °C sarebbe 60 °C: troppo).</summary>
    public const double TemperatureClearMarginC = 5;

    /// <summary>Livello sotto cui un avviso aperto si chiude: soglia − 5 °C per la temperatura, 80% della soglia per il resto.</summary>
    public static double? ClearLevel(ThresholdKind kind, double? threshold) =>
        threshold is not { } t ? null : kind == ThresholdKind.RouterOsTemperature ? t - TemperatureClearMarginC : t * ClearRatio;

    /// <summary>Soglia effettiva: quella specifica (0 = disattivata) oppure quella generale.</summary>
    public static double? Effective(double? specific, double? general) => specific switch
    {
        null => general,
        <= 0 => null,
        { } v => v
    };

    /// <summary>
    /// Campioni minimi per valutare la finestra (metà di quelli attesi con un poll ogni 30 s): con pochi dati
    /// (device appena aggiunto, agente fermo) non si apre né si chiude nulla.
    /// </summary>
    public static int MinSamples(int windowMinutes) => Math.Max(2, windowMinutes);

    /// <summary>Chiave dell'avviso, uguale su apertura e chiusura (Event.AlertKey).</summary>
    public static string Key(ThresholdKind kind, Guid? linkId) => kind switch
    {
        ThresholdKind.LinkUtilization => $"link:{linkId}",
        ThresholdKind.Latency => "latency",
        ThresholdKind.RouterOsCpu => "ros.cpu",
        ThresholdKind.RouterOsTemperature => "ros.temp",
        _ => "loss"
    };
}
