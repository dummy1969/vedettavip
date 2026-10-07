// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Reflection;

namespace VedettaVip.Web;

/// <summary>
/// Sezione <c>App</c> della configurazione. <see cref="SourceUrl"/> è il link "Source code" del footer e della pagina
/// About: chi pubblica una versione modificata deve farlo puntare al proprio sorgente (AGPL-3.0, sezione 13), ad esempio
/// con la variabile d'ambiente <c>App__SourceUrl</c> o in <c>appsettings.Local.json</c>.
/// </summary>
public sealed class AppOptions
{
    public const string Section = "App";
    public const string DefaultSourceUrl = "https://github.com/dummy1969/vedettavip";

    public string? SourceUrl { get; set; }

    /// <summary>URL del sorgente; il default se la configurazione è vuota o non è un URL http(s) assoluto.</summary>
    public string EffectiveSourceUrl =>
        Uri.TryCreate(SourceUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri.ToString()
            : DefaultSourceUrl;

    /// <summary>Versione dell'assembly del Web host (InformationalVersion, senza l'hash del commit se troppo lungo).</summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var assembly = typeof(AppOptions).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? assembly.GetName().Version?.ToString() ?? "?";
        // "0.1.0+<sha completo>": bastano 12 caratteri del commit
        var plus = version.IndexOf('+');
        return plus >= 0 && version.Length > plus + 13 ? version[..(plus + 13)] : version;
    }
}
