// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

namespace VedettaVip.Api.Services;

/// <summary>
/// Posizionamento automatico dei nodi aggiunti senza punto scelto a mano (nuovo device, import CSV): celle di una
/// griglia 8 × N da 170 × 130 a partire da (100, 100), allineate alla griglia della mappa.
/// </summary>
public static class MapPlacement
{
    public const int Columns = 8;
    public const double CellWidth = 170, CellHeight = 130, Margin = 100;

    public static double Snap(double v, int gridSize) => gridSize > 0 ? Math.Round(v / gridSize, MidpointRounding.AwayFromZero) * gridSize : v;

    /// <summary>Centro della cella <paramref name="index"/> (riga per riga) a partire da <paramref name="startY"/>.</summary>
    public static (double X, double Y) Cell(int index, double startY, int gridSize) =>
        (Snap(Margin + index % Columns * CellWidth, gridSize), Snap(startY + index / Columns * CellHeight, gridSize));

    /// <summary>
    /// Posizione di un nodo aggiunto senza punto scelto: sempre <b>sotto</b> i nodi esistenti, così la topologia
    /// disegnata a mano non viene toccata. Mappa vuota: in alto a sinistra. Se l'ultima riga è una "riga dei nuovi
    /// arrivi" (nodi ancora sulle celle della griglia, mai spostati) con posti liberi, la si continua verso destra;
    /// altrimenti si apre una riga nuova sotto il nodo più in basso.
    /// </summary>
    public static (double X, double Y) NextBelow(IReadOnlyCollection<(double X, double Y)> nodes, int gridSize)
    {
        if (nodes.Count == 0)
            return Cell(0, Margin, gridSize);

        var maxY = nodes.Max(n => n.Y);
        var lastRow = nodes.Where(n => maxY - n.Y < CellHeight / 2).ToList();
        var columns = Enumerable.Range(0, Columns).Select(i => Cell(i, maxY, gridSize)).ToList();
        var arrivalRow = lastRow.All(n => n.Y == maxY && columns.Any(c => c.X == n.X));
        if (arrivalRow && columns.FirstOrDefault(c => lastRow.All(n => n.X != c.X)) is { } free && free != default)
            return free;

        return Cell(0, maxY + CellHeight, gridSize);
    }
}
