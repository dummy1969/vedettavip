// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

// Menu contestuale dei nodi: lo tiene dentro la finestra (vicino ai bordi si apre verso l'interno) e gli dà il fuoco
// (Esc lo chiude).
export function fit(element) {
    const r = element.getBoundingClientRect();
    if (r.right > window.innerWidth) element.style.left = Math.max(0, window.innerWidth - r.width - 4) + "px";
    if (r.bottom > window.innerHeight) element.style.top = Math.max(0, window.innerHeight - r.height - 4) + "px";
    element.focus();
}

// Copia negli appunti; navigator.clipboard richiede HTTPS (o localhost), altrimenti il vecchio execCommand.
export async function copyText(text) {
    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        const area = document.createElement("textarea");
        area.value = text;
        area.style.position = "fixed";
        area.style.opacity = "0";
        document.body.appendChild(area);
        area.select();
        const ok = document.execCommand("copy");
        area.remove();
        return ok;
    }
}
