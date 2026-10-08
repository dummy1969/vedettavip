#!/bin/sh
# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (C) 2026 Marcello Anderlini
#
# VedettaVip - gestore dei link winbox:// (WinBox 4) per l'utente corrente, senza sudo.
# Uso:  sh vedettavip-winbox-linux.sh [percorso/di/WinBox]
#       sh vedettavip-winbox-linux.sh --uninstall
set -eu

# Generato da VedettaVip: percorso di WinBox impostato in Impostazioni -> WinBox (vuoto = ricerca nelle posizioni comuni)
CONFIGURED='__WINBOX_PATH__'

BIN="$HOME/.local/bin/vedettavip-winbox"
APPS="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
DESKTOP="$APPS/vedettavip-winbox.desktop"

if [ "${1:-}" = "--uninstall" ]; then
    rm -f "$BIN" "$DESKTOP"
    update-desktop-database "$APPS" 2>/dev/null || true
    echo "Gestore winbox:// rimosso."
    exit 0
fi

# ~/ all'inizio del percorso = cartella home dell'utente
expand() {
    case "$1" in
        "~/"*) printf '%s/%s\n' "$HOME" "${1#\~/}" ;;
        *) printf '%s\n' "$1" ;;
    esac
}

usable() { [ -n "$1" ] && [ -f "$1" ] && [ -x "$1" ]; }

WINBOX=""
if [ -n "${1:-}" ]; then
    WINBOX=$(expand "$1")
    usable "$WINBOX" || { echo "Errore: $WINBOX non esiste o non è eseguibile (chmod +x?)." >&2; exit 1; }
else
    for c in "$(expand "$CONFIGURED")" \
             "$(command -v WinBox 2>/dev/null || true)" "$(command -v winbox 2>/dev/null || true)" \
             "$HOME/WinBox/WinBox" "$HOME/winbox/WinBox" "$HOME/Applications/WinBox/WinBox" \
             "$HOME/.local/share/WinBox/WinBox" "/opt/winbox/WinBox" "/opt/WinBox/WinBox" \
             "$HOME/Downloads/WinBox/WinBox" "$HOME/Scaricati/WinBox/WinBox"; do
        if usable "$c"; then WINBOX="$c"; break; fi
    done
fi

if [ -z "$WINBOX" ]; then
    if [ ! -t 0 ]; then
        echo "Errore: WinBox 4 non trovato; rilanciare indicando il percorso: sh $0 /percorso/di/WinBox" >&2
        exit 1
    fi
    printf 'WinBox 4 non trovato nelle posizioni comuni. Percorso del file WinBox: '
    read -r answer
    WINBOX=$(expand "$answer")
    usable "$WINBOX" || { echo "Errore: $WINBOX non esiste o non è eseguibile (chmod +x?)." >&2; exit 1; }
fi

mkdir -p "$(dirname "$BIN")" "$APPS"

# Il gestore accetta solo winbox://<IP, hostname o MAC>: qualsiasi sito web può aprire un link winbox://
quoted=$(printf '%s' "$WINBOX" | sed "s/'/'\\\\''/g")
cat > "$BIN" <<EOF
#!/bin/sh
# VedettaVip: apre i link winbox://<indirizzo> con WinBox 4. Per rimuoverlo: installer con --uninstall.
t=\${1#winbox:}; t=\${t#//}; t=\${t%%/*}
case "\$t" in
    ''|[!A-Za-z0-9:]*|*[!A-Za-z0-9.:-]*) exit 1 ;;
esac
[ \${#t} -le 253 ] || exit 1
exec '$quoted' "\$t"
EOF
chmod +x "$BIN"

cat > "$DESKTOP" <<EOF
[Desktop Entry]
Type=Application
Name=WinBox (VedettaVip)
Comment=Apre i link winbox:// di VedettaVip con WinBox 4
Exec="$BIN" %u
MimeType=x-scheme-handler/winbox;
NoDisplay=true
Terminal=false
EOF

update-desktop-database "$APPS" 2>/dev/null || true
if command -v xdg-mime >/dev/null 2>&1; then
    xdg-mime default vedettavip-winbox.desktop x-scheme-handler/winbox
else
    echo "Attenzione: xdg-mime non trovato (pacchetto xdg-utils): associare a mano x-scheme-handler/winbox a $DESKTOP." >&2
fi

echo "Fatto: i link winbox:// aprono $WINBOX"
echo "Al primo clic il browser chiede se aprire il link: scegliere \"WinBox (VedettaVip)\" e ricordare la scelta."
