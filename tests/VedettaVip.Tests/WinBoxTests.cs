// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Diagnostics;
using VedettaVip.Api.Endpoints;
using VedettaVip.Api.Services;
using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;

namespace VedettaVip.Tests;

public class WinBoxTests
{
    [Fact]
    public void Windows_installer_has_the_path_quoted_for_powershell_and_crlf_line_endings()
    {
        var script = LinkHandlerScripts.Render("Windows", @"C:\Users\D'Angelo\WinBox.exe", @"C:\Tools\PuTTY's\putty.exe")!;

        Assert.Equal("vedettavip-links-windows.cmd", script.FileName);
        Assert.StartsWith("<# :", script.Text); // la prima riga è insieme etichetta batch e commento PowerShell
        Assert.Contains(@"$ConfiguredWinBox = 'C:\Users\D''Angelo\WinBox.exe'", script.Text);
        Assert.Contains(@"$ConfiguredPutty = 'C:\Tools\PuTTY''s\putty.exe'", script.Text);
        Assert.DoesNotContain(LinkHandlerScripts.WinBoxPlaceholder, script.Text);
        Assert.DoesNotContain(LinkHandlerScripts.PuttyPlaceholder, script.Text);
        Assert.DoesNotContain("\n", script.Text.Replace("\r\n", ""));
    }

    [Fact]
    public void Linux_installer_has_the_path_quoted_for_sh_and_lf_line_endings()
    {
        var script = LinkHandlerScripts.Render("linux", "/home/o'brien/WinBox/WinBox", "/opt/put'ty/putty")!;

        Assert.Equal("vedettavip-links-linux.sh", script.FileName);
        Assert.StartsWith("#!/bin/sh\n", script.Text);
        Assert.Contains(@"CONFIGURED_WINBOX='/home/o'\''brien/WinBox/WinBox'", script.Text);
        Assert.Contains(@"CONFIGURED_PUTTY='/opt/put'\''ty/putty'", script.Text);
        Assert.DoesNotContain("\r", script.Text);
    }

    [Fact]
    public void Without_a_configured_path_the_installers_search_and_unknown_systems_have_none()
    {
        Assert.Contains("$ConfiguredWinBox = ''", LinkHandlerScripts.Render("windows", null, null)!.Text);
        Assert.Contains("$ConfiguredPutty = ''", LinkHandlerScripts.Render("windows", null, null)!.Text);
        Assert.Contains("CONFIGURED_WINBOX=''", LinkHandlerScripts.Render("linux", null, null)!.Text);
        Assert.Contains("CONFIGURED_PUTTY=''", LinkHandlerScripts.Render("linux", null, null)!.Text);
        Assert.Null(LinkHandlerScripts.Render("macos", null, null));
    }

    [Fact]
    public void PowerShell_quoting_doubles_typographic_quotes_too()
    {
        // PowerShell tratta ‘ ’ ‚ ‛ come l'apice: non raddoppiati chiuderebbero la stringa
        Assert.Equal("a''b‘‘c’’d", LinkHandlerScripts.PowerShellQuoted("a'b‘c’d"));
        Assert.Equal(@"it'\''s", LinkHandlerScripts.ShellQuoted("it's"));
    }

    [Theory]
    [InlineData(@"C:\WinBox\WinBox.exe", true, true)]
    [InlineData("%LOCALAPPDATA%\\Programs\\WinBox\\WinBox.exe", true, true)]
    [InlineData("C:\\Win\"Box.exe", true, false)]
    [InlineData("C:\\WinBox\nevil", true, false)]
    [InlineData("~/WinBox/Win\"Box", false, true)]
    [InlineData("/opt/winbox/WinBox\r", false, false)]
    public void Path_validation(string path, bool windows, bool valid) =>
        Assert.Equal(valid, LinkHandlerScripts.Validate(path, windows) is null);

    [Theory]
    [InlineData("192.0.2.1", null, "192.0.2.1")]
    [InlineData("192.0.2.1", 8291, "192.0.2.1")] // porta standard: non serve
    [InlineData(" 192.0.2.1 ", 8292, "192.0.2.1:8292")]
    [InlineData("router.example.com", 18291, "router.example.com:18291")]
    [InlineData("2001:db8::1", 8292, "2001:db8::1")] // IPv6: i gestori installati non accettano le parentesi quadre
    [InlineData("192.0.2.1", 0, "192.0.2.1")]
    [InlineData("192.0.2.1", 70000, "192.0.2.1")]
    public void WinBox_target_adds_only_a_non_standard_port(string address, int? port, string expected) =>
        Assert.Equal(expected, WinBoxTarget.Of(address, port));

    [Theory]
    [InlineData(8292, 9000, 8292)]
    [InlineData(null, 9000, 9000)]
    [InlineData(null, null, null)]
    public void WinBox_port_of_the_device_wins_over_the_general_one(int? device, int? general, int? expected) =>
        Assert.Equal(expected, WinBoxTarget.Effective(device, general));

    [Fact]
    public void RouterOs_api_means_MikroTik()
    {
        Assert.Equal(DeviceVendor.MikroTik, DeviceEndpoints.VendorOf(DeviceVendor.Generic, routerOsApiEnabled: true));
        Assert.Equal(DeviceVendor.MikroTik, DeviceEndpoints.VendorOf(DeviceVendor.MikroTik, routerOsApiEnabled: false));
        Assert.Equal(DeviceVendor.Generic, DeviceEndpoints.VendorOf(DeviceVendor.Generic, routerOsApiEnabled: false));
    }

    [Theory]
    [InlineData(true, null, DeviceVendor.MikroTik)]
    [InlineData(false, "MikroTik", DeviceVendor.MikroTik)]
    [InlineData(false, "Yealink", DeviceVendor.Generic)]
    [InlineData(false, null, DeviceVendor.Generic)]
    public void Discovery_proposes_MikroTik_from_RouterOs_or_mac_vendor(bool routerOs, string? macVendor, DeviceVendor expected)
    {
        var proposal = new DiscoveryDeviceProposalDto("dev:192.0.2.10", "host", "192.0.2.10", null, DeviceType.Other, routerOs,
            null, null, null, [], null, null, null, null, Vendor: macVendor);
        Assert.Equal(expected, DiscoveryPlanner.VendorOf(proposal));
    }

    [Theory]
    [InlineData("", DeviceVendor.Generic)]
    [InlineData("generico", DeviceVendor.Generic)]
    [InlineData("MikroTik", DeviceVendor.MikroTik)]
    [InlineData("RouterOS", DeviceVendor.MikroTik)]
    public void Csv_vendor_values(string text, DeviceVendor expected)
    {
        Assert.True(DeviceCsv.TryParseVendor(text, out var vendor));
        Assert.Equal(expected, vendor);
        Assert.Equal(DeviceCsv.Vendor, DeviceCsv.Canonical("Produttore"));
    }

    [Fact]
    public void Csv_rejects_an_unknown_vendor() => Assert.False(DeviceCsv.TryParseVendor("ubiquiti", out _));

    /// <summary>
    /// Installer Linux vero in una home temporanea, con un finto WinBox che scrive gli argomenti: il gestore passa
    /// l'indirizzo e rifiuta tutto ciò che non è un indirizzo (qualsiasi sito può aprire un link winbox://).
    /// </summary>
    [Fact]
    public async Task Linux_handler_passes_only_addresses_to_winbox()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var home = Directory.CreateTempSubdirectory("vedettavip-winbox-");
        try
        {
            var winboxDir = Path.Combine(home.FullName, "Win Box's");
            Directory.CreateDirectory(winboxDir);
            var log = Path.Combine(home.FullName, "args.log");
            var fakeWinBox = Path.Combine(winboxDir, "WinBox");
            await File.WriteAllTextAsync(fakeWinBox, $"#!/bin/sh\nprintf '%s|' \"$@\" >> '{log}'\necho >> '{log}'\n");
            File.SetUnixFileMode(fakeWinBox, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            var installer = Path.Combine(home.FullName, "install.sh");
            await File.WriteAllTextAsync(installer, LinkHandlerScripts.Render("linux", fakeWinBox, null)!.Text);
            Assert.Equal(0, await RunAsync(home.FullName, "/bin/sh", installer));

            var handler = Path.Combine(home.FullName, ".local", "bin", "vedettavip-winbox");
            Assert.True(File.Exists(Path.Combine(home.FullName, ".local", "share", "applications", "vedettavip-winbox.desktop")));

            Assert.Equal(0, await RunAsync(home.FullName, handler, "winbox://192.0.2.1/"));
            Assert.Equal(0, await RunAsync(home.FullName, handler, "winbox://router.example.com"));
            Assert.Equal(0, await RunAsync(home.FullName, handler, "winbox://192.0.2.1:8292")); // porta non standard
            foreach (var bad in new[] { "winbox://192.0.2.1;touch x", "winbox://$(id)", "winbox://--romon", "winbox://a b", "winbox://" })
                Assert.Equal(1, await RunAsync(home.FullName, handler, bad));

            Assert.Equal(["192.0.2.1|", "router.example.com|", "192.0.2.1:8292|"], await File.ReadAllLinesAsync(log));
        }
        finally
        {
            home.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Gestore SSH/Telnet con un finto PuTTY: arrivano solo protocollo, porta e host; tutto il resto (opzioni travestite da
    /// host, comandi, utenti, porte fuori range, IPv6 malformati, altri schemi) viene rifiutato.
    /// </summary>
    [Fact]
    public async Task Linux_terminal_handler_passes_only_protocol_port_and_host_to_putty()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var home = Directory.CreateTempSubdirectory("vedettavip-putty-");
        try
        {
            var log = Path.Combine(home.FullName, "args.log");
            var fakePutty = await FakeProgramAsync(home.FullName, "Pu TTY's/putty", log);
            var installer = Path.Combine(home.FullName, "install.sh");
            await File.WriteAllTextAsync(installer, LinkHandlerScripts.Render("linux", null, null)!.Text);
            Assert.Equal(0, await RunAsync(home.FullName, "/bin/sh", installer, "--no-winbox", "--putty", fakePutty));

            var apps = Path.Combine(home.FullName, ".local", "share", "applications");
            Assert.True(File.Exists(Path.Combine(apps, "vedettavip-terminal.desktop")));
            Assert.False(File.Exists(Path.Combine(apps, "vedettavip-winbox.desktop"))); // --no-winbox
            var handler = Path.Combine(home.FullName, ".local", "bin", "vedettavip-terminal");

            foreach (var good in new[]
                     {
                         "vedettavip-ssh://192.0.2.1", "vedettavip-ssh://192.0.2.1:2222/", "vedettavip-telnet://router.example.com:23",
                         "vedettavip-ssh://[2001:db8::1]:2222", "vedettavip-telnet://[2001:db8::1]"
                     })
                Assert.Equal(0, await RunAsync(home.FullName, handler, good));
            foreach (var bad in new[]
                     {
                         "vedettavip-ssh://-oProxyCommand=id", "vedettavip-ssh://$(id)", "vedettavip-ssh://host;touch x",
                         "vedettavip-ssh://a b", "vedettavip-ssh://user@host", "vedettavip-ssh://192.0.2.1:0",
                         "vedettavip-ssh://192.0.2.1:65536", "vedettavip-ssh://192.0.2.1:22a", "vedettavip-ssh://host:22:33",
                         "vedettavip-ssh://192.0.2.1:", "vedettavip-ssh://[2001:db8::1]:", "vedettavip-ssh://[2001:db8::1", "vedettavip-ssh://[zz::1]:22",
                         "vedettavip-ssh://[::1]x", "vedettavip-ssh://", "vedettavip-rdp://192.0.2.1", "winbox://192.0.2.1",
                         "vedettavip-ssh://%2DoProxyCommand=id"
                     })
                Assert.Equal(1, await RunAsync(home.FullName, handler, bad));

            Assert.Equal(
            [
                "-ssh|192.0.2.1|", "-ssh|-P|2222|192.0.2.1|", "-telnet|-P|23|router.example.com|", "-ssh|-P|2222|2001:db8::1|",
                "-telnet|2001:db8::1|"
            ], await File.ReadAllLinesAsync(log));
        }
        finally
        {
            home.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Senza PuTTY (o con --terminal) il gestore apre il terminale di sistema con ssh o telnet: un finto x-terminal-emulator in
    /// testa al PATH (il test non deve aprire un terminale vero).
    /// </summary>
    [Fact]
    public async Task Linux_terminal_handler_falls_back_to_the_system_terminal()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var home = Directory.CreateTempSubdirectory("vedettavip-term-");
        try
        {
            var log = Path.Combine(home.FullName, "args.log");
            var fakeTerminal = await FakeProgramAsync(home.FullName, "bin-fake/x-terminal-emulator", log);
            var path = $"{Path.GetDirectoryName(fakeTerminal)}:/usr/bin:/bin";
            var installer = Path.Combine(home.FullName, "install.sh");
            await File.WriteAllTextAsync(installer, LinkHandlerScripts.Render("linux", null, null)!.Text);
            Assert.Equal(0, await RunAsync(home.FullName, "/bin/sh", installer, "--no-winbox", "--terminal"));

            var handler = Path.Combine(home.FullName, ".local", "bin", "vedettavip-terminal");
            Assert.Equal(0, await RunWithPathAsync(home.FullName, path, handler, "vedettavip-ssh://192.0.2.1:2222"));
            Assert.Equal(0, await RunWithPathAsync(home.FullName, path, handler, "vedettavip-ssh://192.0.2.1"));
            Assert.Equal(0, await RunWithPathAsync(home.FullName, path, handler, "vedettavip-telnet://192.0.2.1:2323"));

            Assert.Equal(["-e|ssh|-p|2222|192.0.2.1|", "-e|ssh|192.0.2.1|", "-e|telnet|192.0.2.1|2323|"], await File.ReadAllLinesAsync(log));
        }
        finally
        {
            home.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Gestori Windows (PowerShell) generati dall'installer, eseguiti davvero con pwsh se è disponibile (altrimenti il test
    /// non fa nulla): il testo è estratto dall'installer così come lo scrive sul PC, con un finto PuTTY o WinBox al posto
    /// del programma (percorso con l'apice, raddoppiato come fa l'installer). Stesse regole del gestore Linux.
    /// </summary>
    [Fact]
    public async Task Windows_handlers_pass_only_safe_arguments_when_run_with_powershell()
    {
        if (!OperatingSystem.IsLinux() || FindInPath("pwsh") is not { } pwsh)
            return;

        var home = Directory.CreateTempSubdirectory("vedettavip-pwsh-");
        try
        {
            var log = Path.Combine(home.FullName, "args.log");
            var installer = LinkHandlerScripts.Render("windows", null, null)!.Text.ReplaceLineEndings("\n");

            async Task<string> GenerateAsync(string handlerName, string program)
            {
                // Here-string del gestore (tra @" e "@ dopo la sua Join-Path), valutato da pwsh come fa l'installer, che
                // tiene in $exe il percorso con gli apici già raddoppiati (finisce tra apici nel gestore): qui due volte
                var start = installer.IndexOf($"Join-Path $Dir '{handlerName}'", StringComparison.Ordinal);
                var open = installer.IndexOf("@\"\n", start, StringComparison.Ordinal);
                var close = installer.IndexOf("\n\"@", open, StringComparison.Ordinal) + 3;
                var output = Path.Combine(home.FullName, handlerName);
                var generator = Path.Combine(home.FullName, "gen-" + handlerName);
                await File.WriteAllTextAsync(generator,
                    $"$exe = '{LinkHandlerScripts.PowerShellQuoted(LinkHandlerScripts.PowerShellQuoted(program))}'\n$handlerText = {installer[open..close]}\n" +
                    $"Set-Content -LiteralPath '{LinkHandlerScripts.PowerShellQuoted(output)}' -Value $handlerText\n");
                Assert.Equal(0, await RunAsync(home.FullName, pwsh, "-NoProfile", "-File", generator));
                return output;
            }

            var puttyHandler = await GenerateAsync("putty-handler.ps1", await FakeProgramAsync(home.FullName, "Pu TTY's/putty", log));
            var winboxHandler = await GenerateAsync("winbox-handler.ps1", await FakeProgramAsync(home.FullName, "Win Box/WinBox", log));

            foreach (var good in new[]
                     {
                         "vedettavip-ssh://192.0.2.1", "vedettavip-ssh://192.0.2.1:2222/", "vedettavip-telnet://router.example.com:23",
                         "vedettavip-ssh://[2001:db8::1]:2222"
                     })
                Assert.Equal(0, await RunAsync(home.FullName, pwsh, "-NoProfile", "-File", puttyHandler, good));
            foreach (var bad in new[]
                     {
                         "vedettavip-ssh://-oProxyCommand=id", "vedettavip-ssh://%2DoProxyCommand=id", "vedettavip-ssh://$(id)",
                         "vedettavip-ssh://host;touch x", "vedettavip-ssh://a b", "vedettavip-ssh://user@host",
                         "vedettavip-ssh://192.0.2.1:0", "vedettavip-ssh://192.0.2.1:65536", "vedettavip-ssh://host:22:33",
                         "vedettavip-ssh://192.0.2.1:", "vedettavip-ssh://[2001:db8::1]:", "vedettavip-ssh://[zz::1]:22",
                         "vedettavip-ssh://", "vedettavip-rdp://192.0.2.1", "winbox://192.0.2.1"
                     })
                Assert.Equal(1, await RunAsync(home.FullName, pwsh, "-NoProfile", "-File", puttyHandler, bad));
            Assert.Equal(0, await RunAsync(home.FullName, pwsh, "-NoProfile", "-File", winboxHandler, "winbox://192.0.2.1:8292/"));
            Assert.Equal(1, await RunAsync(home.FullName, pwsh, "-NoProfile", "-File", winboxHandler, "winbox://--romon"));

            // Start-Process non aspetta il programma: si attendono le righe nel log
            string[] expected =
            [
                "-ssh|192.0.2.1|", "-ssh|-P|2222|192.0.2.1|", "-telnet|-P|23|router.example.com|", "-ssh|-P|2222|2001:db8::1|",
                "192.0.2.1:8292|"
            ];
            var lines = Array.Empty<string>();
            for (var i = 0; i < 100 && lines.Length < expected.Length; i++)
            {
                await Task.Delay(100);
                lines = File.Exists(log) ? await File.ReadAllLinesAsync(log) : [];
            }
            Assert.Equal(expected.Order(), lines.Order());
        }
        finally
        {
            home.Delete(recursive: true);
        }
    }

    private static string? FindInPath(string program) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(dir => Path.Combine(dir, program))
        .FirstOrDefault(File.Exists);

    /// <summary>Programma finto che scrive i propri argomenti, separati da |, una riga per avvio.</summary>
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static async Task<string> FakeProgramAsync(string home, string relativePath, string log)
    {
        var file = Path.Combine(home, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, $"#!/bin/sh\nprintf '%s|' \"$@\" >> '{log}'\necho >> '{log}'\n");
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return file;
    }

    private static Task<int> RunAsync(string home, string file, params string[] arguments) => RunWithPathAsync(home, null, file, arguments);

    private static async Task<int> RunWithPathAsync(string home, string? path, string file, params string[] arguments)
    {
        var start = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        if (path is not null)
            start.Environment["PATH"] = path;
        start.Environment["HOME"] = home;
        start.Environment["XDG_DATA_HOME"] = Path.Combine(home, ".local", "share");
        start.Environment["XDG_CONFIG_HOME"] = Path.Combine(home, ".config");
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        await Task.WhenAll(output, errors); // letti fino in fondo: un processo verboso non si blocca sul pipe pieno
        return process.ExitCode;
    }
}
