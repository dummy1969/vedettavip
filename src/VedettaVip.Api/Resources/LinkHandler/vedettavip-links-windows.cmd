<# : VedettaVip - gestore dei link (winbox:// con WinBox 4, vedettavip-ssh:// e vedettavip-telnet:// con PuTTY), file unico: la parte batch avvia la parte PowerShell
@echo off
rem SPDX-License-Identifier: AGPL-3.0-or-later
rem Copyright (C) 2026 Marcello Anderlini
rem
rem Doppio clic per installare (solo per l'utente corrente, senza permessi da amministratore).
rem Per rimuoverlo: aprire un prompt nella cartella del file e lanciarlo con /uninstall.
set "VV_SELF=%~f0"
set "VV_ARGS=%*"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "iex ([IO.File]::ReadAllText($env:VV_SELF, [Text.Encoding]::UTF8))"
echo.
pause
exit /b
#>

# Generato da VedettaVip: percorsi impostati in Impostazioni -> Accesso remoto (vuoto = ricerca nelle posizioni comuni)
$ConfiguredWinBox = '__WINBOX_PATH__'
$ConfiguredPutty = '__PUTTY_PATH__'

$ErrorActionPreference = 'Stop'
$Dir = Join-Path $env:LOCALAPPDATA 'VedettaVip'
$Classes = 'HKCU:\Software\Classes'
$Schemes = @('winbox', 'vedettavip-ssh', 'vedettavip-telnet')
# Apici tipografici: PowerShell li tratta come l'apice, nel percorso scritto tra apici vanno raddoppiati
$Quotes = "['$([char]0x2018)$([char]0x2019)$([char]0x201A)$([char]0x201B)]"

# Primo percorso esistente; se nessuno, finestra "Apri file" (Annulla = programma saltato, restituisce $null)
function Find-Program([string]$Configured, [string[]]$Candidates, [string]$Title, [string]$File) {
    $list = @()
    if ($Configured) { $list += [Environment]::ExpandEnvironmentVariables($Configured) }
    $list += $Candidates
    $found = $list | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -First 1
    if ($found) { return $found }
    Write-Host "$Title non trovato nelle posizioni comuni: sceglilo nella finestra che si apre (Annulla = salta $Title)."
    Add-Type -AssemblyName System.Windows.Forms
    $dialog = New-Object System.Windows.Forms.OpenFileDialog
    $dialog.Title = "Dove si trova $Title ($File)? Annulla = non usarlo"
    $dialog.Filter = "$Title (*.exe)|*.exe"
    if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { return $dialog.FileName }
    return $null
}

function Register-Scheme([string]$Scheme, [string]$Description, [string]$Handler) {
    $key = "$Classes\$Scheme"
    New-Item -Path "$key\shell\open\command" -Force | Out-Null
    Set-Item -LiteralPath $key -Value "URL:$Description"
    New-ItemProperty -LiteralPath $key -Name 'URL Protocol' -Value '' -PropertyType String -Force | Out-Null
    Set-Item -LiteralPath "$key\shell\open\command" `
        -Value ('powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "{0}" "%1"' -f $Handler)
}

try {
    if ($env:VV_ARGS -match '(^|\s)[/-]+uninstall\b') {
        foreach ($scheme in $Schemes) { Remove-Item -LiteralPath "$Classes\$scheme" -Recurse -Force -ErrorAction SilentlyContinue }
        Remove-Item -LiteralPath $Dir -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host 'Gestore dei link di VedettaVip rimosso (winbox://, vedettavip-ssh://, vedettavip-telnet://).'
        exit 0
    }

    $WinBox = Find-Program $ConfiguredWinBox @(
        "$env:ProgramFiles\MikroTik\WinBox\WinBox.exe",
        "$env:ProgramFiles\WinBox\WinBox.exe",
        "$env:LOCALAPPDATA\Programs\WinBox\WinBox.exe",
        "$env:LOCALAPPDATA\MikroTik\WinBox\WinBox.exe",
        "$env:USERPROFILE\WinBox\WinBox.exe",
        "$env:USERPROFILE\Desktop\WinBox.exe",
        "$env:USERPROFILE\Downloads\WinBox.exe",
        "$env:USERPROFILE\Downloads\WinBox_Windows\WinBox.exe") 'WinBox 4' 'WinBox.exe'

    $puttyInPath = Get-Command putty.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty Source
    $Putty = Find-Program $ConfiguredPutty @(
        "$env:ProgramFiles\PuTTY\putty.exe",
        "${env:ProgramFiles(x86)}\PuTTY\putty.exe",
        "$env:LOCALAPPDATA\Programs\PuTTY\putty.exe",
        "$env:USERPROFILE\PuTTY\putty.exe",
        "$env:USERPROFILE\Desktop\putty.exe",
        "$env:USERPROFILE\Downloads\putty.exe",
        $puttyInPath) 'PuTTY' 'putty.exe'

    if (-not $WinBox -and -not $Putty) { throw 'Né WinBox né PuTTY selezionati: installazione annullata.' }
    New-Item -ItemType Directory -Force -Path $Dir | Out-Null

    if ($WinBox) {
        # Il gestore accetta solo winbox://<IP, hostname o MAC>[:porta]: qualsiasi sito web puo' aprire un link winbox://
        $exe = $WinBox -replace "($Quotes)", '$1$1'
        $handler = Join-Path $Dir 'winbox-handler.ps1'
        $handlerText = @"
# VedettaVip: apre i link winbox://<indirizzo> con WinBox 4. Per rimuoverlo: installer con /uninstall.
param([string]`$Url)
`$t = [Uri]::UnescapeDataString((`$Url -replace '^winbox:(//)?', '' -replace '/.*$', ''))
if (`$t -notmatch '^[A-Za-z0-9:][A-Za-z0-9.:-]{0,252}$') { exit 1 }
Start-Process -FilePath '$exe' -ArgumentList `$t
"@
        Set-Content -LiteralPath $handler -Value $handlerText -Encoding UTF8
        Register-Scheme 'winbox' 'WinBox (VedettaVip)' $handler
        Write-Host "WinBox: i link winbox:// aprono $WinBox"
    }
    else {
        Write-Host 'WinBox saltato: i link winbox:// restano come prima.'
    }

    if ($Putty) {
        # Il gestore accetta solo <host o [IPv6]>[:porta 1-65535] e passa a PuTTY soltanto -ssh/-telnet, -P e l'host
        $exe = $Putty -replace "($Quotes)", '$1$1'
        $handler = Join-Path $Dir 'putty-handler.ps1'
        $handlerText = @"
# VedettaVip: apre i link vedettavip-ssh:// e vedettavip-telnet:// con PuTTY. Per rimuoverlo: installer con /uninstall.
param([string]`$Url)
if (`$Url -notmatch '^vedettavip-(ssh|telnet)://([^/?#]+)') { exit 1 }
`$proto = `$Matches[1].ToLowerInvariant()
`$target = [Uri]::UnescapeDataString(`$Matches[2])
if (`$target -cmatch '^\[([0-9A-Fa-f:.]+)\](?::([0-9]{1,5}))?$' -or `$target -cmatch '^([A-Za-z0-9][A-Za-z0-9.-]{0,252})(?::([0-9]{1,5}))?$') {
    `$hostName = `$Matches[1]
    `$port = `$Matches[2]
}
else { exit 1 }
`$arguments = @("-`$proto")
if (`$port) {
    if ([int]`$port -lt 1 -or [int]`$port -gt 65535) { exit 1 }
    `$arguments += @('-P', [string][int]`$port)
}
`$arguments += `$hostName
Start-Process -FilePath '$exe' -ArgumentList `$arguments
"@
        Set-Content -LiteralPath $handler -Value $handlerText -Encoding UTF8
        Register-Scheme 'vedettavip-ssh' 'SSH con PuTTY (VedettaVip)' $handler
        Register-Scheme 'vedettavip-telnet' 'Telnet con PuTTY (VedettaVip)' $handler
        Write-Host "PuTTY: i link vedettavip-ssh:// e vedettavip-telnet:// aprono $Putty"
    }
    else {
        Write-Host 'PuTTY saltato: i link SSH e Telnet restano come prima.'
    }

    Write-Host 'Al primo clic il browser chiede se aprire il programma: spuntare "Ricorda" (o "Consenti sempre").'
}
catch {
    Write-Host "Errore: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
