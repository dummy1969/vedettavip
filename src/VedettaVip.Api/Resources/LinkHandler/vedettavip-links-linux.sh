#!/bin/sh
# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (C) 2026 Marcello Anderlini
#
# VedettaVip - gestore dei link per l'utente corrente, senza sudo:
#   winbox://                               -> WinBox 4
#   vedettavip-ssh://, vedettavip-telnet:// -> PuTTY, oppure il terminale di sistema con ssh/telnet
# Uso:  sh vedettavip-links-linux.sh [--winbox PERCORSO] [--putty PERCORSO] [--terminal] [--no-winbox]
#       sh vedettavip-links-linux.sh --uninstall
#   --terminal   SSH/Telnet nel terminale di sistema anche se PuTTY è installato
#   --no-winbox  non registrare winbox:// (resta com'era)
#   Un percorso senza opzione vale come --winbox (compatibile con il vecchio installer di WinBox).
set -eu

# Generato da VedettaVip: percorsi impostati in Impostazioni -> Accesso remoto (vuoto = ricerca nelle posizioni comuni)
CONFIGURED_WINBOX='__WINBOX_PATH__'
CONFIGURED_PUTTY='__PUTTY_PATH__'

BIN_DIR="$HOME/.local/bin"
APPS="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
WINBOX_BIN="$BIN_DIR/vedettavip-winbox"
WINBOX_DESKTOP="$APPS/vedettavip-winbox.desktop"
TERMINAL_BIN="$BIN_DIR/vedettavip-terminal"
TERMINAL_DESKTOP="$APPS/vedettavip-terminal.desktop"

WINBOX_ARG=""
PUTTY_ARG=""
USE_TERMINAL=0
SKIP_WINBOX=0
while [ $# -gt 0 ]; do
    case "$1" in
        --uninstall)
            rm -f "$WINBOX_BIN" "$WINBOX_DESKTOP" "$TERMINAL_BIN" "$TERMINAL_DESKTOP"
            update-desktop-database "$APPS" 2>/dev/null || true
            echo "Gestore dei link di VedettaVip rimosso (winbox://, vedettavip-ssh://, vedettavip-telnet://)."
            exit 0 ;;
        --winbox|--putty)
            [ $# -ge 2 ] || { echo "Errore: $1 richiede un percorso." >&2; exit 1; }
            if [ "$1" = "--winbox" ]; then WINBOX_ARG=$2; else PUTTY_ARG=$2; fi
            shift ;;
        --terminal) USE_TERMINAL=1 ;;
        --no-winbox) SKIP_WINBOX=1 ;;
        -*) echo "Errore: opzione sconosciuta $1" >&2; exit 1 ;;
        *) WINBOX_ARG=$1 ;;
    esac
    shift
done

# ~/ all'inizio del percorso = cartella home dell'utente
expand() {
    case "$1" in
        "~/"*) printf '%s/%s\n' "$HOME" "${1#\~/}" ;;
        *) printf '%s\n' "$1" ;;
    esac
}

usable() { [ -n "$1" ] && [ -f "$1" ] && [ -x "$1" ]; }

# Contenuto di una stringa sh tra apici
quote() { printf '%s' "$1" | sed "s/'/'\\\\''/g"; }

mkdir -p "$BIN_DIR" "$APPS"
registered=""

# ---------- WinBox ----------
WINBOX=""
if [ "$SKIP_WINBOX" = 0 ]; then
    if [ -n "$WINBOX_ARG" ]; then
        WINBOX=$(expand "$WINBOX_ARG")
        usable "$WINBOX" || { echo "Errore: $WINBOX non esiste o non è eseguibile (chmod +x?)." >&2; exit 1; }
    else
        for c in "$(expand "$CONFIGURED_WINBOX")" \
                 "$(command -v WinBox 2>/dev/null || true)" "$(command -v winbox 2>/dev/null || true)" \
                 "$HOME/WinBox/WinBox" "$HOME/winbox/WinBox" "$HOME/Applications/WinBox/WinBox" \
                 "$HOME/.local/share/WinBox/WinBox" "/opt/winbox/WinBox" "/opt/WinBox/WinBox" \
                 "$HOME/Downloads/WinBox/WinBox" "$HOME/Scaricati/WinBox/WinBox"; do
            if usable "$c"; then WINBOX="$c"; break; fi
        done
        if [ -z "$WINBOX" ] && [ -t 0 ]; then
            printf 'WinBox 4 non trovato nelle posizioni comuni. Percorso del file WinBox (invio = salta): '
            read -r answer
            if [ -n "$answer" ]; then
                WINBOX=$(expand "$answer")
                usable "$WINBOX" || { echo "Errore: $WINBOX non esiste o non è eseguibile (chmod +x?)." >&2; exit 1; }
            fi
        fi
    fi
fi

if [ -n "$WINBOX" ]; then
    # Il gestore accetta solo winbox://<IP, hostname o MAC>[:porta]: qualsiasi sito web può aprire un link winbox://
    cat > "$WINBOX_BIN" <<EOF
#!/bin/sh
# VedettaVip: apre i link winbox://<indirizzo> con WinBox 4. Per rimuoverlo: installer con --uninstall.
t=\${1#winbox:}; t=\${t#//}; t=\${t%%/*}
case "\$t" in
    ''|[!A-Za-z0-9:]*|*[!A-Za-z0-9.:-]*) exit 1 ;;
esac
[ \${#t} -le 253 ] || exit 1
exec '$(quote "$WINBOX")' "\$t"
EOF
    chmod +x "$WINBOX_BIN"
    cat > "$WINBOX_DESKTOP" <<EOF
[Desktop Entry]
Type=Application
Name=WinBox (VedettaVip)
Comment=Apre i link winbox:// di VedettaVip con WinBox 4
Exec="$WINBOX_BIN" %u
MimeType=x-scheme-handler/winbox;
NoDisplay=true
Terminal=false
EOF
    registered="$registered winbox"
    echo "WinBox: i link winbox:// aprono $WINBOX"
else
    echo "WinBox non configurato: i link winbox:// restano come prima."
fi

# ---------- PuTTY o terminale di sistema ----------
PUTTY=""
if [ "$USE_TERMINAL" = 0 ]; then
    if [ -n "$PUTTY_ARG" ]; then
        PUTTY=$(expand "$PUTTY_ARG")
        usable "$PUTTY" || { echo "Errore: $PUTTY non esiste o non è eseguibile." >&2; exit 1; }
    else
        for c in "$(expand "$CONFIGURED_PUTTY")" "$(command -v putty 2>/dev/null || true)" "/usr/bin/putty" "/snap/bin/putty"; do
            if usable "$c"; then PUTTY="$c"; break; fi
        done
    fi
fi

# Gestore dei link SSH/Telnet: accetta solo <host o [IPv6]>[:porta 1-65535] e passa al programma soltanto protocollo,
# porta e host (qualsiasi sito web può aprire questi link)
{
    echo '#!/bin/sh'
    echo '# VedettaVip: apre i link vedettavip-ssh:// e vedettavip-telnet:// (PuTTY o terminale). Per rimuoverlo: installer con --uninstall.'
    printf "PUTTY='%s'\n" "$(quote "$PUTTY")"
    cat <<'EOF'
case "${1:-}" in
    vedettavip-ssh://*) proto=ssh ;;
    vedettavip-telnet://*) proto=telnet ;;
    *) exit 1 ;;
esac
t=${1#*://}; t=${t%%/*}
port=""
case "$t" in
    \[*)
        host=${t#\[}; rest=${host#*\]}
        [ "$rest" != "$host" ] || exit 1
        host=${host%%\]*}
        case "$host" in ''|*[!0-9A-Fa-f:.]*) exit 1 ;; esac
        case "$rest" in '') ;; :?*) port=${rest#:} ;; *) exit 1 ;; esac ;;
    *)
        host=${t%%:*}
        case "$t" in *:) exit 1 ;; *:*) port=${t#*:} ;; esac
        case "$host" in ''|[!A-Za-z0-9]*|*[!A-Za-z0-9.-]*) exit 1 ;; esac ;;
esac
[ ${#host} -le 253 ] || exit 1
if [ -n "$port" ]; then
    case "$port" in *[!0-9]*) exit 1 ;; esac
    [ ${#port} -le 5 ] && [ "$port" -ge 1 ] && [ "$port" -le 65535 ] || exit 1
fi

if [ -n "$PUTTY" ]; then
    if [ -n "$port" ]; then exec "$PUTTY" "-$proto" -P "$port" "$host"; fi
    exec "$PUTTY" "-$proto" "$host"
fi

if [ "$proto" = ssh ]; then
    set -- ssh
    [ -z "$port" ] || set -- "$@" -p "$port"
    set -- "$@" "$host"
else
    set -- telnet "$host"
    [ -z "$port" ] || set -- "$@" "$port"
fi
for term in x-terminal-emulator gnome-terminal konsole xfce4-terminal mate-terminal tilix xterm; do
    command -v "$term" >/dev/null 2>&1 || continue
    case "$term" in
        gnome-terminal|mate-terminal|tilix) exec "$term" -- "$@" ;;
        xfce4-terminal) exec "$term" -x "$@" ;;
        *) exec "$term" -e "$@" ;;
    esac
done
echo "VedettaVip: nessun terminale trovato per $proto" >&2
exit 1
EOF
} > "$TERMINAL_BIN"
chmod +x "$TERMINAL_BIN"
cat > "$TERMINAL_DESKTOP" <<EOF
[Desktop Entry]
Type=Application
Name=SSH e Telnet (VedettaVip)
Comment=Apre i link vedettavip-ssh:// e vedettavip-telnet:// di VedettaVip
Exec="$TERMINAL_BIN" %u
MimeType=x-scheme-handler/vedettavip-ssh;x-scheme-handler/vedettavip-telnet;
NoDisplay=true
Terminal=false
EOF
registered="$registered vedettavip-ssh vedettavip-telnet"
if [ -n "$PUTTY" ]; then
    echo "SSH e Telnet: i link vedettavip-ssh:// e vedettavip-telnet:// aprono $PUTTY"
else
    echo "SSH e Telnet: PuTTY non trovato (o --terminal), i link aprono il terminale di sistema con ssh/telnet."
    command -v ssh >/dev/null 2>&1 || echo "Attenzione: il comando ssh non è installato (pacchetto openssh-client)." >&2
fi

# ---------- Associazione degli schemi ----------
update-desktop-database "$APPS" 2>/dev/null || true
if command -v xdg-mime >/dev/null 2>&1; then
    for scheme in $registered; do
        case "$scheme" in
            winbox) xdg-mime default vedettavip-winbox.desktop x-scheme-handler/winbox ;;
            *) xdg-mime default vedettavip-terminal.desktop "x-scheme-handler/$scheme" ;;
        esac
    done
else
    echo "Attenzione: xdg-mime non trovato (pacchetto xdg-utils): associare a mano gli schemi$registered ai file .desktop in $APPS." >&2
fi

echo "Al primo clic il browser chiede se aprire il link: scegliere il gestore di VedettaVip e ricordare la scelta."
