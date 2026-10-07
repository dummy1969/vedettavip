// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

// Unica funzione JS della mappa: posizione dell'SVG nella pagina, per convertire
// le coordinate del puntatore (client) in coordinate della mappa.
export function bounds(element) {
    const r = element.getBoundingClientRect();
    return { left: r.left, top: r.top, width: r.width, height: r.height };
}
