// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Text.RegularExpressions;

namespace VedettaVip.Api.Services;

/// <summary>
/// Installer del gestore dei link winbox:// (Resources/WinBox): il browser non può avviare programmi, ma apre i protocolli
/// registrati sul PC. Ogni installer ha il percorso di WinBox impostato in VedettaVip scritto dentro (al posto di
/// <see cref="Placeholder"/>, in una stringa tra apici), così l'operatore non deve indicarlo.
/// </summary>
public static partial class WinBoxHandlerScripts
{
    public const string Placeholder = "__WINBOX_PATH__";

    public sealed record Script(string FileName, string ContentType, string Text);

    /// <summary>Installer per "windows" (cmd + PowerShell in un file) o "linux" (sh); null se il sistema non è previsto.</summary>
    public static Script? Render(string os, string? winBoxPath) => os.ToLowerInvariant() switch
    {
        "windows" => new Script("vedettavip-winbox-windows.cmd", "application/octet-stream",
            Load("vedettavip-winbox-windows.cmd").Replace(Placeholder, PowerShellQuoted(winBoxPath ?? "")).ReplaceLineEndings("\r\n")),
        "linux" => new Script("vedettavip-winbox-linux.sh", "text/x-shellscript; charset=utf-8",
            Load("vedettavip-winbox-linux.sh").Replace(Placeholder, ShellQuoted(winBoxPath ?? "")).ReplaceLineEndings("\n")),
        _ => null
    };

    /// <summary>
    /// Contenuto di una stringa PowerShell tra apici: ogni apice (anche quelli tipografici, che PowerShell tratta allo
    /// stesso modo) va raddoppiato.
    /// </summary>
    public static string PowerShellQuoted(string value) => PowerShellQuote().Replace(value, "$0$0");

    /// <summary>Contenuto di una stringa sh tra apici: l'apice diventa '\'' (chiude, apice escapato, riapre).</summary>
    public static string ShellQuoted(string value) => value.Replace("'", "'\\''");

    /// <summary>Errore del percorso (null = valido). Niente caratteri di controllo (a capo); su Windows niente virgolette.</summary>
    public static string? Validate(string? path, bool windows)
    {
        if (path is null)
            return null;
        if (path.Any(char.IsControl))
            return "Il percorso contiene caratteri non ammessi (a capo, tabulazioni).";
        if (windows && (path.Contains('"') || path.IndexOfAny(['<', '>', '|', '?', '*']) >= 0))
            return "Il percorso Windows contiene caratteri non ammessi in un nome di file (\" < > | ? *).";
        return null;
    }

    private static string Load(string name)
    {
        using var stream = typeof(WinBoxHandlerScripts).Assembly.GetManifestResourceStream($"VedettaVip.Api.WinBox.{name}")
                           ?? throw new InvalidOperationException($"Risorsa {name} mancante");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [GeneratedRegex("['‘’‚‛]")]
    private static partial Regex PowerShellQuote();
}
