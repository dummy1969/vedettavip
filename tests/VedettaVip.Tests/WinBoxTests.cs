// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Diagnostics;
using VedettaVip.Api.Endpoints;
using VedettaVip.Api.Services;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Tests;

public class WinBoxTests
{
    [Fact]
    public void Windows_installer_has_the_path_quoted_for_powershell_and_crlf_line_endings()
    {
        var script = WinBoxHandlerScripts.Render("Windows", @"C:\Users\D'Angelo\WinBox.exe")!;

        Assert.Equal("vedettavip-winbox-windows.cmd", script.FileName);
        Assert.StartsWith("<# :", script.Text); // la prima riga è insieme etichetta batch e commento PowerShell
        Assert.Contains(@"$Configured = 'C:\Users\D''Angelo\WinBox.exe'", script.Text);
        Assert.DoesNotContain(WinBoxHandlerScripts.Placeholder, script.Text);
        Assert.DoesNotContain("\n", script.Text.Replace("\r\n", ""));
    }

    [Fact]
    public void Linux_installer_has_the_path_quoted_for_sh_and_lf_line_endings()
    {
        var script = WinBoxHandlerScripts.Render("linux", "/home/o'brien/WinBox/WinBox")!;

        Assert.Equal("vedettavip-winbox-linux.sh", script.FileName);
        Assert.StartsWith("#!/bin/sh\n", script.Text);
        Assert.Contains(@"CONFIGURED='/home/o'\''brien/WinBox/WinBox'", script.Text);
        Assert.DoesNotContain("\r", script.Text);
    }

    [Fact]
    public void Without_a_configured_path_the_installers_search_and_unknown_systems_have_none()
    {
        Assert.Contains("$Configured = ''", WinBoxHandlerScripts.Render("windows", null)!.Text);
        Assert.Contains("CONFIGURED=''", WinBoxHandlerScripts.Render("linux", null)!.Text);
        Assert.Null(WinBoxHandlerScripts.Render("macos", null));
    }

    [Fact]
    public void PowerShell_quoting_doubles_typographic_quotes_too()
    {
        // PowerShell tratta ‘ ’ ‚ ‛ come l'apice: non raddoppiati chiuderebbero la stringa
        Assert.Equal("a''b‘‘c’’d", WinBoxHandlerScripts.PowerShellQuoted("a'b‘c’d"));
        Assert.Equal(@"it'\''s", WinBoxHandlerScripts.ShellQuoted("it's"));
    }

    [Theory]
    [InlineData(@"C:\WinBox\WinBox.exe", true, true)]
    [InlineData("%LOCALAPPDATA%\\Programs\\WinBox\\WinBox.exe", true, true)]
    [InlineData("C:\\Win\"Box.exe", true, false)]
    [InlineData("C:\\WinBox\nevil", true, false)]
    [InlineData("~/WinBox/Win\"Box", false, true)]
    [InlineData("/opt/winbox/WinBox\r", false, false)]
    public void Path_validation(string path, bool windows, bool valid) =>
        Assert.Equal(valid, WinBoxHandlerScripts.Validate(path, windows) is null);

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
            await File.WriteAllTextAsync(installer, WinBoxHandlerScripts.Render("linux", fakeWinBox)!.Text);
            Assert.Equal(0, await RunAsync(home.FullName, "/bin/sh", installer));

            var handler = Path.Combine(home.FullName, ".local", "bin", "vedettavip-winbox");
            Assert.True(File.Exists(Path.Combine(home.FullName, ".local", "share", "applications", "vedettavip-winbox.desktop")));

            Assert.Equal(0, await RunAsync(home.FullName, handler, "winbox://192.0.2.1/"));
            Assert.Equal(0, await RunAsync(home.FullName, handler, "winbox://router.example.com"));
            foreach (var bad in new[] { "winbox://192.0.2.1;touch x", "winbox://$(id)", "winbox://--romon", "winbox://a b", "winbox://" })
                Assert.Equal(1, await RunAsync(home.FullName, handler, bad));

            Assert.Equal(["192.0.2.1|", "router.example.com|"], await File.ReadAllLinesAsync(log));
        }
        finally
        {
            home.Delete(recursive: true);
        }
    }

    private static async Task<int> RunAsync(string home, string file, string argument)
    {
        var start = new ProcessStartInfo(file) { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(argument);
        start.Environment["HOME"] = home;
        start.Environment["XDG_DATA_HOME"] = Path.Combine(home, ".local", "share");
        start.Environment["XDG_CONFIG_HOME"] = Path.Combine(home, ".config");
        using var process = Process.Start(start)!;
        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
