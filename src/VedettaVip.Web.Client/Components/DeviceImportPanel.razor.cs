// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using VedettaVip.Shared.Contracts;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Components;

/// <summary>
/// Import CSV in due passi: anteprima (nessuna scrittura) e conferma. Qualunque modifica al testo o alle opzioni
/// annulla l'anteprima, così si importa sempre esattamente ciò che si è visto.
/// </summary>
public partial class DeviceImportPanel : IDisposable
{
    [Inject] private VedettaVipApiClient Api { get; set; } = default!;
    [Inject] private CurrentUser User { get; set; } = default!;

    /// <summary>Notifica un import riuscito (la pagina ricarica l'elenco).</summary>
    [Parameter] public EventCallback OnImported { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private const string Template =
        "nome;indirizzo;tipo;snmp;cliente;padre;profilo_snmp;abilitato\r\n" +
        "Router sede;192.0.2.1;router;v2c;Cliente Esempio;;;si\r\n" +
        "Switch piano 1;192.0.2.2;switch;v2c;Cliente Esempio;Router sede;;si\r\n";

    private static readonly string TemplateHref =
        "data:text/csv;charset=utf-8," + Uri.EscapeDataString("﻿" + Template);

    private readonly CancellationTokenSource cts = new();
    private IReadOnlyList<MapSummaryDto> maps = [];
    private string csv = "";
    private string? fileInfo;
    private ImportDuplicateMode onDuplicate = ImportDuplicateMode.Skip;
    private string mapId = "";
    private bool createCustomers;
    private DeviceImportResultDto? result;
    private bool errorsOnly;
    private bool busy;
    private string? error;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            maps = [.. (await Api.GetMapsAsync(cts.Token)).OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)];
        }
        catch (HttpRequestException ex)
        {
            error = $"Mappe non caricate: {ex.Message}";
        }
    }

    /// <summary>UTF-8 (con o senza BOM); se non valido, Latin-1 (CSV salvati da Excel con la codifica di Windows).</summary>
    private async Task LoadFileAsync(InputFileChangeEventArgs e)
    {
        error = null;
        try
        {
            using var stream = e.File.OpenReadStream(DeviceImportRequestDto.MaxCsvLength);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cts.Token);
            var bytes = buffer.ToArray();
            string encoding;
            try
            {
                csv = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
                encoding = "UTF-8";
            }
            catch (DecoderFallbackException)
            {
                csv = Encoding.Latin1.GetString(bytes);
                encoding = "Windows/Latin-1";
            }
            csv = csv.TrimStart('﻿');
            fileInfo = $"{e.File.Name}: {bytes.Length / 1024.0:0.#} KB, codifica {encoding}";
            Invalidate();
        }
        catch (IOException ex)
        {
            error = $"File non letto ({ex.Message}): massimo {DeviceImportRequestDto.MaxCsvLength / 1024 / 1024} MB.";
        }
    }

    private void Invalidate()
    {
        result = null;
        errorsOnly = false;
    }

    private DeviceImportRequestDto Request(bool dryRun) =>
        new(csv, dryRun, onDuplicate, Guid.TryParse(mapId, out var m) ? m : null, createCustomers && User.IsAdmin);

    private async Task PreviewAsync() => await RunAsync(async () =>
    {
        result = await Api.ImportDevicesAsync(Request(dryRun: true), cts.Token);
        errorsOnly = result.Errors > 0 && result.Errors < result.Rows.Count;
    });

    private async Task ImportAsync() => await RunAsync(async () =>
    {
        result = await Api.ImportDevicesAsync(Request(dryRun: false), cts.Token);
        errorsOnly = false;
        if (result.Error is null)
            await OnImported.InvokeAsync();
    });

    private async Task RunAsync(Func<Task> action)
    {
        busy = true;
        error = null;
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
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

    private static string ActionClass(ImportRowAction a) => a switch
    {
        ImportRowAction.Create => "text-bg-success",
        ImportRowAction.Update => "text-bg-primary",
        ImportRowAction.Skip => "text-bg-secondary",
        _ => "text-bg-danger"
    };

    private static string ActionText(ImportRowAction a, bool dryRun) => a switch
    {
        ImportRowAction.Create => dryRun ? "nuovo" : "creato",
        ImportRowAction.Update => dryRun ? "aggiorna" : "aggiornato",
        ImportRowAction.Skip => "saltato",
        _ => "errore"
    };

    public void Dispose()
    {
        cts.Cancel();
        cts.Dispose();
    }
}
