// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using VedettaVip.Shared.Contracts;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Components;

/// <summary>
/// Impostazioni → Accesso remoto: porte predefinite di WinBox, HTTP, HTTPS, SSH e Telnet (i dispositivi possono averne una
/// propria) e percorsi di WinBox 4 e PuTTY scritti negli installer del gestore dei link.
/// </summary>
public partial class RemoteAccessSettingsCard
{
    [Inject] private VedettaVipApiClient Api { get; set; } = default!;

    private bool loaded;
    private bool busy;
    private string? error;
    private DateTime? savedAt;
    private string? winBoxWindowsPath;
    private string? winBoxLinuxPath;
    private string? puttyWindowsPath;
    private string? puttyLinuxPath;
    private int? winBoxPort;
    private int? httpPort;
    private int? httpsPort;
    private int? sshPort;
    private int? telnetPort;
    /// <summary>Valori salvati: quelli che finiscono negli installer scaricati.</summary>
    private RemoteAccessSettingsDto? saved;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            Show(await Api.GetRemoteAccessSettingsAsync(CancellationToken.None));
            loaded = true;
        }
        catch (HttpRequestException ex)
        {
            error = $"Impostazioni di accesso remoto non caricate: {ex.Message}";
        }
    }

    private void Show(RemoteAccessSettingsDto s)
    {
        saved = s;
        (winBoxWindowsPath, winBoxLinuxPath, puttyWindowsPath, puttyLinuxPath) =
            (s.WinBoxWindowsPath, s.WinBoxLinuxPath, s.PuttyWindowsPath, s.PuttyLinuxPath);
        (winBoxPort, httpPort, httpsPort, sshPort, telnetPort) = (s.WinBoxPort, s.HttpPort, s.HttpsPort, s.SshPort, s.TelnetPort);
    }

    private async Task SaveAsync()
    {
        busy = true;
        error = null;
        savedAt = null;
        try
        {
            Show(await Api.UpdateRemoteAccessSettingsAsync(new RemoteAccessSettingsDto(winBoxWindowsPath, winBoxLinuxPath, winBoxPort,
                puttyWindowsPath, puttyLinuxPath, httpPort, httpsPort, sshPort, telnetPort), CancellationToken.None));
            savedAt = DateTime.Now;
        }
        catch (HttpRequestException ex)
        {
            error = ex.Message;
        }
        finally
        {
            busy = false;
        }
    }
}
