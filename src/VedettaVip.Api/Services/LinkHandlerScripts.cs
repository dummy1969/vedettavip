// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Text.RegularExpressions;

namespace VedettaVip.Api.Services;

/// <summary>
/// Installer del gestore dei link di VedettaVip (Resources/LinkHandler): il browser non può avviare programmi, ma apre i
/// protocolli registrati sul PC. Lo stesso installer registra winbox:// (WinBox 4) e vedettavip-ssh:// / vedettavip-telnet://
/// (PuTTY; su Linux in mancanza il terminale con ssh/telnet). I percorsi impostati in VedettaVip sono scritti dentro (al posto
/// di <see cref="WinBoxPlaceholder"/> e <see cref="PuttyPlaceholder"/>, in stringhe tra apici): l'operatore non li indica.
/// </summary>
public static partial class LinkHandlerScripts
{
    public const string WinBoxPlaceholder = "__WINBOX_PATH__";
    public const string PuttyPlaceholder = "__PUTTY_PATH__";

    public sealed record Script(string FileName, string ContentType, string Text);

    /// <summary>Installer per "windows" (cmd + PowerShell in un file) o "linux" (sh); null se il sistema non è previsto.</summary>
    public static Script? Render(string os, string? winBoxPath, string? puttyPath) => os.ToLowerInvariant() switch
    {
        "windows" => new Script("vedettavip-links-windows.cmd", "application/octet-stream",
            Load("vedettavip-links-windows.cmd")
                .Replace(WinBoxPlaceholder, PowerShellQuoted(winBoxPath ?? ""))
                .Replace(PuttyPlaceholder, PowerShellQuoted(puttyPath ?? ""))
                .ReplaceLineEndings("\r\n")),
        "linux" => new Script("vedettavip-links-linux.sh", "text/x-shellscript; charset=utf-8",
            Load("vedettavip-links-linux.sh")
                .Replace(WinBoxPlaceholder, ShellQuoted(winBoxPath ?? ""))
                .Replace(PuttyPlaceholder, ShellQuoted(puttyPath ?? ""))
                .ReplaceLineEndings("\n")),
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
        using var stream = typeof(LinkHandlerScripts).Assembly.GetManifestResourceStream($"VedettaVip.Api.LinkHandler.{name}")
                           ?? throw new InvalidOperationException($"Risorsa {name} mancante");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [GeneratedRegex("['‘’‚‛]")]
    private static partial Regex PowerShellQuote();
}
