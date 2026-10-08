// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using VedettaVip.Shared.Contracts;
using VedettaVip.Web.Client.Services;

namespace VedettaVip.Web.Client.Components;

/// <summary>
/// Form di creazione/modifica di un dispositivo. SnmpCredentialId e le altre proprietà
/// sono tutte nel form: al salvataggio il PUT le sostituisce (il PUT altrimenti li azzererebbe,
/// o ripristinerebbe un valore vecchio se cambiati altrove dopo il caricamento della pagina).
/// </summary>
public partial class DeviceEditor : IDisposable
{
    [Inject] private VedettaVipApiClient Api { get; set; } = default!;
    [Inject] private ILogger<DeviceEditor> Logger { get; set; } = default!;

    /// <summary>Dispositivo da modificare; null = nuovo.</summary>
    [Parameter] public DeviceDto? Device { get; set; }
    /// <summary>Tutti i dispositivi: per la scelta del padre e il controllo degli indirizzi duplicati.</summary>
    [Parameter, EditorRequired] public IReadOnlyList<DeviceDto> AllDevices { get; set; } = [];
    /// <summary>Soglie generali, mostrate come segnaposto dei campi della sezione Rilevazione.</summary>
    [Parameter] public DetectionThresholdsDto? Defaults { get; set; }
    /// <summary>Clienti selezionabili (instradamento delle notifiche).</summary>
    [Parameter] public IReadOnlyList<CustomerDto> Customers { get; set; } = [];
    /// <summary>Profili SNMP selezionabili (la community non arriva mai al browser).</summary>
    [Parameter] public IReadOnlyList<SnmpCredentialDto> SnmpProfiles { get; set; } = [];
    /// <summary>Profili RouterOS selezionabili (la password non arriva mai al browser).</summary>
    [Parameter] public IReadOnlyList<RouterOsCredentialDto> RouterOsProfiles { get; set; } = [];
    /// <summary>Soglie generali sulle metriche, mostrate come segnaposto.</summary>
    [Parameter] public MetricThresholdsDto? Metrics { get; set; }
    /// <summary>Notifica il salvataggio con l'Id del dispositivo.</summary>
    [Parameter] public EventCallback<Guid> OnSaved { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private enum EditorTab { General, Monitoring, RouterOs, Icon }

    private static readonly (EditorTab Tab, string Label)[] Tabs =
    [
        (EditorTab.General, "Generale"), (EditorTab.Monitoring, "Monitoraggio"), (EditorTab.RouterOs, "RouterOS"), (EditorTab.Icon, "Icona")
    ];

    // Stato: select testuale (un select legato a un bool in Blazor non si aggancia in modo affidabile)
    private const string Monitored = "on", NotMonitored = "off";

    private readonly CancellationTokenSource cts = new();
    private EditorTab tab = EditorTab.General;
    private DeviceDto? loaded;
    private bool initialized;
    private bool busy;
    private string? error;

    private string name = "";
    private string address = "";
    private DeviceType type = DeviceType.Other;
    private SnmpVersion snmpVersion = SnmpVersion.None;
    private string parentId = "";
    private string customerId = "";
    private string snmpCredentialId = "";
    private string routerOsCredentialId = "";
    private string mapId = "";
    /// <summary>Icona del dispositivo; null = predefinita del tipo (un nodo può comunque sceglierne una sua).</summary>
    private string? icon;
    private IReadOnlyList<MapSummaryDto> maps = [];
    private int? rttThresholdMs;
    private double? lossThresholdPct;
    private bool routerOsApiEnabled;
    private DeviceVendor vendor = DeviceVendor.Generic;

    /// <summary>L'API RouterOS esiste solo sui MikroTik: abilitarla fissa il produttore (l'API fa lo stesso).</summary>
    private bool RouterOsApiEnabled
    {
        get => routerOsApiEnabled;
        set
        {
            routerOsApiEnabled = value;
            if (value)
                vendor = DeviceVendor.MikroTik;
        }
    }
    private bool enabled = true;
    private string EnabledText { get => enabled ? Monitored : NotMonitored; set => enabled = value == Monitored; }
    private int? downAfterFailures;
    private int? upAfterSuccesses;
    private int? snmpDegradedAfterFailures;
    private int? cpuThresholdPct;
    private int? temperatureThresholdC;
    private int watchCount;

    /// <summary>Le mappe servono solo alla creazione ("Aggiungi alla mappa").</summary>
    protected override async Task OnInitializedAsync()
    {
        if (Device is not null)
            return;
        try
        {
            maps = await Api.GetMapsAsync(cts.Token);
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning("Mappe non caricate: {Message}", ex.Message); // il device si crea comunque, senza mappa
        }
    }

    protected override void OnParametersSet()
    {
        // Ricarica i campi solo quando cambia il dispositivo, non a ogni aggiornamento della pagina
        if (initialized && Device?.Id == loaded?.Id)
            return;

        initialized = true;
        tab = EditorTab.General;
        mapId = "";
        loaded = Device;
        error = null;
        name = Device?.Name ?? "";
        address = Device?.Address ?? "";
        type = Device?.Type ?? DeviceType.Router;
        snmpVersion = Device?.SnmpVersion ?? SnmpVersion.V2c;
        parentId = Device?.ParentDeviceId?.ToString() ?? "";
        customerId = Device?.CustomerId?.ToString() ?? "";
        snmpCredentialId = Device?.SnmpCredentialId?.ToString() ?? "";
        routerOsCredentialId = Device?.RouterOsCredentialId?.ToString() ?? "";
        icon = Device?.Icon;
        rttThresholdMs = Device?.RttThresholdMs;
        lossThresholdPct = Device?.LossThresholdPct;
        routerOsApiEnabled = Device?.RouterOsApiEnabled ?? false;
        vendor = Device?.Vendor ?? DeviceVendor.Generic;
        enabled = Device?.Enabled ?? true;
        downAfterFailures = Device?.DownAfterFailures;
        upAfterSuccesses = Device?.UpAfterSuccesses;
        snmpDegradedAfterFailures = Device?.SnmpDegradedAfterFailures;
        cpuThresholdPct = Device?.CpuThresholdPct;
        temperatureThresholdC = Device?.TemperatureThresholdC;
        watchCount = 0;
    }

    /// <summary>Segnale sulla scheda quando contiene valori diversi dai predefiniti (es. "3": soglie personalizzate).</summary>
    private (string Text, string Title)? TabBadge(EditorTab t)
    {
        switch (t)
        {
            case EditorTab.Monitoring:
                var custom = new object?[] { downAfterFailures, upAfterSuccesses, snmpDegradedAfterFailures, rttThresholdMs, lossThresholdPct }
                    .Count(v => v is not null);
                return custom > 0 ? (custom.ToString(), $"{custom} soglie specifiche di questo dispositivo") : null;
            case EditorTab.RouterOs:
                return !routerOsApiEnabled ? null
                    : watchCount > 0 ? ($"on · {watchCount}", $"lettura RouterOS attiva, {watchCount} elementi sorvegliati")
                    : ("on", "lettura RouterOS attiva");
            case EditorTab.Icon:
                return icon is not null ? ("•", "icona scelta per questo dispositivo") : null;
            default:
                return null;
        }
    }

    private bool CanSave() => !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(address);

    private string? DuplicateAddressOf()
    {
        var a = address.Trim();
        if (a.Length == 0) return null;
        var other = AllDevices.FirstOrDefault(d => d.Id != Device?.Id && string.Equals(d.Address, a, StringComparison.OrdinalIgnoreCase));
        return other?.Name;
    }

    /// <summary>Padri ammessi: tutti tranne il dispositivo stesso e i suoi discendenti (creerebbero un ciclo).</summary>
    private IEnumerable<DeviceDto> ParentCandidates()
    {
        if (Device is null)
            return AllDevices;

        var excluded = new HashSet<Guid> { Device.Id };
        bool added;
        do
        {
            added = false;
            foreach (var d in AllDevices)
                if (d.ParentDeviceId is { } p && excluded.Contains(p) && excluded.Add(d.Id))
                    added = true;
        }
        while (added);

        return AllDevices.Where(d => !excluded.Contains(d.Id));
    }

    private async Task SaveAsync()
    {
        if (!CanSave()) return;

        busy = true;
        error = null;
        try
        {
            var dto = new DeviceUpsertDto(
                name.Trim(),
                address.Trim(),
                type,
                icon,
                snmpVersion,
                snmpVersion is SnmpVersion.V1 or SnmpVersion.V2c && Guid.TryParse(snmpCredentialId, out var cred) ? cred : null,
                routerOsApiEnabled,
                Guid.TryParse(parentId, out var p) ? p : null,
                enabled,
                downAfterFailures,
                upAfterSuccesses,
                snmpDegradedAfterFailures,
                Guid.TryParse(customerId, out var c) ? c : null,
                rttThresholdMs,
                lossThresholdPct,
                Guid.TryParse(routerOsCredentialId, out var ros) ? ros : null,
                routerOsApiEnabled ? cpuThresholdPct : null,
                routerOsApiEnabled ? temperatureThresholdC : null,
                vendor);

            Guid id;
            if (Device is null)
            {
                id = (await Api.CreateDeviceAsync(dto, cts.Token, Guid.TryParse(mapId, out var m) ? m : null)).Id;
            }
            else
            {
                await Api.UpdateDeviceAsync(Device.Id, dto, cts.Token);
                id = Device.Id;
            }

            await OnSaved.InvokeAsync(id);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
        }
        catch (HttpRequestException ex)
        {
            Logger.LogWarning(ex, "Salvataggio del dispositivo fallito");
            error = ex.Message;
        }
        finally
        {
            busy = false;
        }
    }

    /// <summary>Voce "nessun profilo proprio": dice quale profilo si eredita (cliente, predefinito o configurazione del Worker).</summary>
    private string InheritedProfileText()
    {
        var customer = Customers.FirstOrDefault(c => c.Id.ToString() == customerId);
        if (customer?.SnmpCredentialId is { } fromCustomer)
            return $"dal cliente: {SnmpProfiles.FirstOrDefault(p => p.Id == fromCustomer)?.Name ?? "?"}";
        return SnmpProfiles.FirstOrDefault(p => p.IsDefault) is { } d
            ? $"predefinito: {d.Name}"
            : "predefinito (community della configurazione del Worker)";
    }

    /// <summary>Voce "nessun profilo proprio" del RouterOS: dice cosa si eredita.</summary>
    private string InheritedRouterOsText()
    {
        var customer = Customers.FirstOrDefault(c => c.Id.ToString() == customerId);
        if (customer?.RouterOsCredentialId is { } fromCustomer)
            return $"dal cliente: {RouterOsProfiles.FirstOrDefault(p => p.Id == fromCustomer)?.Name ?? "?"}";
        return RouterOsProfiles.FirstOrDefault(p => p.IsDefault) is { } d
            ? $"predefinito: {d.Name}"
            : "nessun profilo: crearne uno in Impostazioni → Profili RouterOS";
    }

    private static string Placeholder(int? general) => general is { } g ? $"generale: {g}" : "generale";

    private static string IntPlaceholder(int? general) => general is { } g ? $"generale: {g}" : "generale: disattivata";

    private static string MetricPlaceholder(double? general) => general is { } g ? $"generale: {g:0.#}" : "generale: disattivata";

    internal static string TypeLabel(DeviceType t) => t switch
    {
        DeviceType.Router => "Router",
        DeviceType.Switch => "Switch",
        DeviceType.AccessPoint => "Access point",
        DeviceType.Server => "Server",
        DeviceType.Firewall => "Firewall",
        DeviceType.Storage => "Storage / NAS",
        DeviceType.Pc => "PC",
        DeviceType.Printer => "Stampante",
        DeviceType.Camera => "Telecamera",
        DeviceType.Phone => "Telefono VoIP",
        DeviceType.Ups => "UPS",
        _ => "Altro"
    };

    internal static string VendorLabel(DeviceVendor v) => v switch
    {
        DeviceVendor.MikroTik => "MikroTik",
        _ => "Generico"
    };

    public void Dispose()
    {
        cts.Cancel();
        cts.Dispose();
    }
}
