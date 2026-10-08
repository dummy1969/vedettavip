<# : VedettaVip - gestore dei link winbox:// (WinBox 4), file unico: la parte batch avvia la parte PowerShell
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

# Generato da VedettaVip: percorso di WinBox impostato in Impostazioni -> WinBox (vuoto = ricerca nelle posizioni comuni)
$Configured = '__WINBOX_PATH__'

$ErrorActionPreference = 'Stop'
$Dir = Join-Path $env:LOCALAPPDATA 'VedettaVip'
$Handler = Join-Path $Dir 'winbox-handler.ps1'
$Key = 'HKCU:\Software\Classes\winbox'

try {
    if ($env:VV_ARGS -match '(^|\s)[/-]+uninstall\b') {
        Remove-Item -LiteralPath $Key -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $Dir -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host 'Gestore winbox:// rimosso.'
        exit 0
    }

    $candidates = @()
    if ($Configured) { $candidates += [Environment]::ExpandEnvironmentVariables($Configured) }
    $candidates += @(
        "$env:ProgramFiles\MikroTik\WinBox\WinBox.exe",
        "$env:ProgramFiles\WinBox\WinBox.exe",
        "$env:LOCALAPPDATA\Programs\WinBox\WinBox.exe",
        "$env:LOCALAPPDATA\MikroTik\WinBox\WinBox.exe",
        "$env:USERPROFILE\WinBox\WinBox.exe",
        "$env:USERPROFILE\Desktop\WinBox.exe",
        "$env:USERPROFILE\Downloads\WinBox.exe",
        "$env:USERPROFILE\Downloads\WinBox_Windows\WinBox.exe")
    $WinBox = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -First 1

    if (-not $WinBox) {
        Write-Host 'WinBox 4 non trovato nelle posizioni comuni: scegli WinBox.exe nella finestra che si apre.'
        Add-Type -AssemblyName System.Windows.Forms
        $dialog = New-Object System.Windows.Forms.OpenFileDialog
        $dialog.Title = 'Dove si trova WinBox 4 (WinBox.exe)?'
        $dialog.Filter = 'WinBox (*.exe)|*.exe'
        if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { throw 'WinBox non selezionato: installazione annullata.' }
        $WinBox = $dialog.FileName
    }

    # Il gestore accetta solo winbox://<IP, hostname o MAC>: qualsiasi sito web puo' aprire un link winbox://
    $quotes = "['$([char]0x2018)$([char]0x2019)$([char]0x201A)$([char]0x201B)]"
    $exe = $WinBox -replace "($quotes)", '$1$1'
    $handlerText = @"
# VedettaVip: apre i link winbox://<indirizzo> con WinBox 4. Per rimuoverlo: installer con /uninstall.
param([string]`$Url)
`$t = [Uri]::UnescapeDataString((`$Url -replace '^winbox:(//)?', '' -replace '/.*$', ''))
if (`$t -notmatch '^[A-Za-z0-9:][A-Za-z0-9.:-]{0,252}$') { exit 1 }
Start-Process -FilePath '$exe' -ArgumentList `$t
"@
    New-Item -ItemType Directory -Force -Path $Dir | Out-Null
    Set-Content -LiteralPath $Handler -Value $handlerText -Encoding UTF8

    New-Item -Path "$Key\shell\open\command" -Force | Out-Null
    Set-Item -LiteralPath $Key -Value 'URL:WinBox (VedettaVip)'
    New-ItemProperty -LiteralPath $Key -Name 'URL Protocol' -Value '' -PropertyType String -Force | Out-Null
    Set-Item -LiteralPath "$Key\shell\open\command" `
        -Value ('powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "{0}" "%1"' -f $Handler)

    Write-Host "Fatto: i link winbox:// aprono $WinBox"
    Write-Host 'Al primo clic il browser chiede se aprire WinBox: spuntare "Ricorda" (o "Consenti sempre").'
}
catch {
    Write-Host "Errore: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
