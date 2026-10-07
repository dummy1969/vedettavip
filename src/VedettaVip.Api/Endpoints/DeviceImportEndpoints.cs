// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using VedettaVip.Api.Data;
using VedettaVip.Api.Data.Entities;
using VedettaVip.Api.Security;
using VedettaVip.Api.Services;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Endpoints;

/// <summary>
/// Import ed export CSV dei dispositivi (formato in <see cref="DeviceCsv"/>). L'import si fa in due passi: anteprima
/// (DryRun, nessuna scrittura) e conferma; le righe valide vengono salvate in un unico SaveChanges (una transazione).
/// Duplicati riconosciuti per indirizzo. Una colonna presente nel file sovrascrive il campo anche se vuota
/// (cliente, padre, profilo vuoti = nessuno); per non toccare un campo negli aggiornamenti si toglie la colonna.
/// </summary>
public static class DeviceImportEndpoints
{
    private const int MaxParentDepth = 64;

    public static IEndpointRouteBuilder MapDeviceImportEndpoints(this IEndpointRouteBuilder app)
    {
        var devices = app.MapGroup("/api/devices").WithTags("Devices");
        devices.MapPost("/import", ImportAsync).RequireOperator();
        devices.MapGet("/export", ExportAsync);
        return app;
    }

    /// <summary>Piano di una riga del file.</summary>
    private sealed class Plan(DeviceCsv.Row row)
    {
        public DeviceCsv.Row Row { get; } = row;
        public string Name { get; } = row.Get(DeviceCsv.Name);
        public string Address { get; } = row.Get(DeviceCsv.Address);
        public ImportRowAction Action { get; set; } = ImportRowAction.Create;
        public List<string> Errors { get; } = [];
        public List<string> Notes { get; } = [];
        public Device? Existing { get; set; }
        public Guid Id { get; set; }
        public DeviceType Type { get; set; }
        public SnmpVersion Snmp { get; set; }
        public bool Enabled { get; set; } = true;
        public Guid? CustomerId { get; set; }
        public string? NewCustomerName { get; set; }
        public Guid? ProfileId { get; set; }
        public Guid? ParentId { get; set; }
        public Plan? ParentPlan { get; set; }
        public bool Valid => Action is ImportRowAction.Create or ImportRowAction.Update;

        public void Fail(string message)
        {
            Errors.Add(message);
            Action = ImportRowAction.Error;
        }
    }

    private static async Task<Results<Ok<DeviceImportResultDto>, ValidationProblem, ProblemHttpResult>> ImportAsync(
        DeviceImportRequestDto request, ClaimsPrincipal user, VedettaVipDbContext db, AgentNotifier agents, CancellationToken ct)
    {
        if (request.CreateMissingCustomers && !user.IsInRole(Roles.Admin))
            return DbProblems.Problem(StatusCodes.Status403Forbidden, "Solo un amministratore può creare clienti");

        DeviceCsv.Table table;
        try
        {
            table = DeviceCsv.Parse(request.Csv);
        }
        catch (FormatException ex)
        {
            return TypedResults.Ok(Empty(request.DryRun, ex.Message));
        }
        if (table.Rows.Count > DeviceImportRequestDto.MaxRows)
            return TypedResults.Ok(Empty(request.DryRun, $"Troppe righe ({table.Rows.Count}): massimo {DeviceImportRequestDto.MaxRows} per import."));
        if (table.Rows.Count == 0)
            return TypedResults.Ok(Empty(request.DryRun, "Nessuna riga di dati dopo l'intestazione."));

        Map? map = null;
        if (request.MapId is { } mapId && (map = await db.Maps.FirstOrDefaultAsync(m => m.Id == mapId, ct)) is null)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { [nameof(request.MapId)] = ["La mappa non esiste."] });

        var devices = await db.Devices.ToListAsync(ct); // tracciati: aggiornati sul posto alla conferma
        var byAddress = devices.ToLookup(d => d.Address, StringComparer.OrdinalIgnoreCase);
        var customers = (await db.Customers.AsNoTracking().Select(c => new { c.Id, c.Name }).ToListAsync(ct))
            .ToDictionary(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase);
        var profiles = (await db.SnmpCredentials.AsNoTracking().Select(c => new { c.Id, c.Name }).ToListAsync(ct))
            .ToDictionary(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase);

        var plans = table.Rows.Select(r => new Plan(r)).ToList();
        var firstLineByAddress = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // ---------- 1. Valori della singola riga ----------
        foreach (var p in plans)
        {
            var row = p.Row;
            if (p.Name.Length == 0) p.Fail("nome obbligatorio");
            else if (p.Name.Length > 128) p.Fail("nome oltre 128 caratteri");

            if (p.Address.Length == 0) p.Fail("indirizzo obbligatorio");
            else if (p.Address.Length > 255 || !DeviceEndpoints.IsValidAddress(p.Address)) p.Fail($"indirizzo non valido \"{p.Address}\"");
            else if (!firstLineByAddress.TryAdd(p.Address, row.Line)) p.Fail($"indirizzo già presente alla riga {firstLineByAddress[p.Address]}");

            if (DeviceCsv.TryParseType(row.Get(DeviceCsv.Type), out var type)) p.Type = type;
            else p.Fail($"tipo \"{row.Get(DeviceCsv.Type)}\" sconosciuto (router, switch, accesspoint, server, firewall, storage, pc, printer, camera, phone, ups, other)");

            if (DeviceCsv.TryParseSnmp(row.Get(DeviceCsv.Snmp), out var snmp)) p.Snmp = snmp;
            else p.Fail($"SNMP \"{row.Get(DeviceCsv.Snmp)}\" sconosciuto (none, v1, v2c, v3)");

            if (DeviceCsv.TryParseBool(row.Get(DeviceCsv.Enabled), out var enabled)) p.Enabled = enabled;
            else p.Fail($"abilitato \"{row.Get(DeviceCsv.Enabled)}\" non valido (si/no)");

            var customer = row.Get(DeviceCsv.Customer);
            if (customer.Length > 0)
            {
                if (customers.TryGetValue(customer, out var cid)) p.CustomerId = cid;
                else if (request.CreateMissingCustomers && customer.Length <= 128) p.NewCustomerName = customer;
                else p.Fail($"cliente \"{customer}\" inesistente (crearlo in Impostazioni → Clienti o scegliere \"crea i clienti mancanti\")");
            }

            var profile = row.Get(DeviceCsv.SnmpProfile);
            if (profile.Length > 0)
            {
                if (profiles.TryGetValue(profile, out var pid)) p.ProfileId = pid;
                else p.Fail($"profilo SNMP \"{profile}\" inesistente (Impostazioni → Profili SNMP)");
            }

            var existing = p.Address.Length > 0 ? byAddress[p.Address].ToList() : [];
            if (existing.Count > 1)
                p.Fail($"{existing.Count} dispositivi hanno già l'indirizzo {p.Address}: aggiornamento ambiguo");
            else if (existing.Count == 1)
            {
                p.Existing = existing[0];
                p.Id = existing[0].Id;
                if (p.Action != ImportRowAction.Error)
                {
                    p.Action = request.OnDuplicate == ImportDuplicateMode.Update ? ImportRowAction.Update : ImportRowAction.Skip;
                    p.Notes.Add(p.Action == ImportRowAction.Update ? $"aggiorna \"{existing[0].Name}\"" : $"esiste già: \"{existing[0].Name}\"");
                }
            }
            else
            {
                p.Id = Guid.CreateVersion7();
            }
        }

        // ---------- 2. Padri: per indirizzo, poi per nome, tra i device esistenti e quelli del file ----------
        var fileByAddress = plans.Where(p => p.Action != ImportRowAction.Error && p.Existing is null)
            .ToDictionary(p => p.Address, StringComparer.OrdinalIgnoreCase);
        var planById = plans.Where(p => p.Action != ImportRowAction.Error).ToDictionary(p => p.Id);
        // Nomi finali: quelli del file sostituiscono i nomi attuali dei device aggiornati
        var finalNames = devices.Where(d => !planById.ContainsKey(d.Id) || planById[d.Id].Action == ImportRowAction.Skip)
            .Select(d => (d.Name, d.Id))
            .Concat(plans.Where(p => p.Valid).Select(p => (p.Name, p.Id)))
            .ToLookup(x => x.Name, x => x.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var p in plans.Where(p => p.Valid && p.Row.Get(DeviceCsv.Parent).Length > 0))
        {
            var reference = p.Row.Get(DeviceCsv.Parent);
            Guid? parentId = byAddress[reference].Select(d => (Guid?)d.Id).FirstOrDefault()
                             ?? (fileByAddress.TryGetValue(reference, out var fp) ? fp.Id : null);
            if (parentId is null)
            {
                var named = finalNames[reference].Distinct().ToList();
                if (named.Count > 1) { p.Fail($"padre \"{reference}\" ambiguo ({named.Count} dispositivi con questo nome): usare l'indirizzo"); continue; }
                parentId = named.Count == 1 ? named[0] : null;
            }
            if (parentId is null && plans.FirstOrDefault(x => x.Action == ImportRowAction.Error
                    && (string.Equals(x.Address, reference, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(x.Name, reference, StringComparison.OrdinalIgnoreCase))) is { } failed)
                p.Fail($"il padre \"{reference}\" (riga {failed.Row.Line}) ha errori");
            else if (parentId is null) p.Fail($"padre \"{reference}\" inesistente (né tra i dispositivi né nel file)");
            else if (parentId == p.Id) p.Fail("un dispositivo non può dipendere da se stesso");
            else
            {
                p.ParentId = parentId;
                p.ParentPlan = plans.FirstOrDefault(x => x.Id == parentId && x.Existing is null);
            }
        }

        // ---------- 3. Cicli e righe che dipendono da righe in errore (fino a stabilità) ----------
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var p in plans.Where(p => p.Valid && p.ParentPlan is { Action: ImportRowAction.Error }))
            {
                p.Fail($"il padre (riga {p.ParentPlan!.Row.Line}) ha errori");
                changed = true;
            }

            var parentOf = devices.ToDictionary(d => d.Id, d => d.ParentDeviceId);
            foreach (var p in plans.Where(p => p.Valid))
                parentOf[p.Id] = p.Row.Has(DeviceCsv.Parent) ? p.ParentId : p.Existing?.ParentDeviceId;
            foreach (var p in plans.Where(p => p.Valid))
            {
                var current = parentOf.GetValueOrDefault(p.Id);
                for (var depth = 0; current is { } id; depth++)
                {
                    if (id == p.Id || depth >= MaxParentDepth) { p.Fail("le dipendenze (padre) formano un ciclo"); changed = true; break; }
                    current = parentOf.GetValueOrDefault(id);
                }
            }
        }

        // ---------- 4. Mappa: dispositivi validi non ancora presenti ----------
        var onMap = map is null ? [] : (await db.MapNodes.AsNoTracking().Where(n => n.MapId == map.Id && n.DeviceId != null)
            .Select(n => n.DeviceId!.Value).ToListAsync(ct)).ToHashSet();
        var toMap = map is null ? [] : plans.Where(p => p.Valid && !onMap.Contains(p.Id)).ToList();
        foreach (var p in toMap)
            p.Notes.Add($"aggiunto alla mappa \"{map!.Name}\"");
        foreach (var p in plans.Where(p => p.Valid && p.NewCustomerName is not null))
            p.Notes.Add($"nuovo cliente \"{p.NewCustomerName}\"");

        if (!request.DryRun)
        {
            Apply(db, plans);
            await PlaceAsync(db, map, toMap, ct);
            if (await db.TrySaveChangesAsync(ct) is { } problem)
                return problem;
            await agents.TargetsChangedAsync();
        }

        return TypedResults.Ok(new DeviceImportResultDto(
            request.DryRun,
            plans.Count(p => p.Action == ImportRowAction.Create),
            plans.Count(p => p.Action == ImportRowAction.Update),
            plans.Count(p => p.Action == ImportRowAction.Skip),
            plans.Count(p => p.Action == ImportRowAction.Error),
            toMap.Count,
            table.Columns,
            table.Ignored,
            plans.Select(p => new DeviceImportRowDto(p.Row.Line, p.Name, p.Address, p.Action,
                p.Errors.Count > 0 ? string.Join("; ", p.Errors) : p.Notes.Count > 0 ? string.Join("; ", p.Notes) : null)).ToList()));
    }

    private static void Apply(VedettaVipDbContext db, List<Plan> plans)
    {
        var newCustomers = new Dictionary<string, Customer>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in plans.Where(p => p.Valid))
        {
            if (p.NewCustomerName is { } customerName)
            {
                if (!newCustomers.TryGetValue(customerName, out var customer))
                {
                    customer = new Customer { Id = Guid.CreateVersion7(), Name = customerName };
                    newCustomers[customerName] = customer;
                    db.Customers.Add(customer);
                }
                p.CustomerId = customer.Id;
            }

            var row = p.Row;
            var d = p.Existing;
            if (d is null)
            {
                d = new Device { Id = p.Id, Name = p.Name, Address = p.Address };
                db.Devices.Add(d);
            }
            d.Name = p.Name;
            d.Address = p.Address;
            var isNew = p.Existing is null;
            if (isNew || row.Has(DeviceCsv.Type)) d.Type = p.Type;
            if (isNew || row.Has(DeviceCsv.Snmp)) d.SnmpVersion = p.Snmp;
            if (isNew || row.Has(DeviceCsv.Enabled)) d.Enabled = p.Enabled;
            if (isNew || row.Has(DeviceCsv.Customer)) d.CustomerId = p.CustomerId;
            if (isNew || row.Has(DeviceCsv.SnmpProfile)) d.SnmpCredentialId = p.ProfileId;
            if (isNew || row.Has(DeviceCsv.Parent)) d.ParentDeviceId = p.ParentId;
        }
    }

    /// <summary>Nodi a griglia sotto quelli esistenti (8 per riga), allineati alla griglia della mappa.</summary>
    private static async Task PlaceAsync(VedettaVipDbContext db, Map? map, List<Plan> toMap, CancellationToken ct)
    {
        if (map is null || toMap.Count == 0)
            return;
        var maxY = await db.MapNodes.Where(n => n.MapId == map.Id).MaxAsync(n => (double?)n.Y, ct);
        var startY = maxY is { } y ? y + MapPlacement.CellHeight : MapPlacement.Margin;

        for (var i = 0; i < toMap.Count; i++)
        {
            var (x, cy) = MapPlacement.Cell(i, startY, map.GridSize);
            db.MapNodes.Add(new MapNode
            {
                Id = Guid.CreateVersion7(),
                MapId = map.Id,
                Kind = MapNodeKind.Device,
                DeviceId = toMap[i].Id,
                X = x,
                Y = cy,
                LabelTemplate = MapNode.DefaultLabelTemplate
            });
        }
    }

    /// <summary>Tutti i dispositivi nel formato dell'import (padre per nome se univoco, altrimenti per indirizzo).</summary>
    private static async Task<FileContentHttpResult> ExportAsync(VedettaVipDbContext db, TimeProvider time, CancellationToken ct)
    {
        var devices = await db.Devices.AsNoTracking()
            .OrderBy(d => d.Name)
            .Select(d => new
            {
                d.Id, d.Name, d.Address, d.Type, d.SnmpVersion, d.Enabled, d.ParentDeviceId,
                Customer = d.Customer != null ? d.Customer.Name : "",
                Profile = d.SnmpCredential != null ? d.SnmpCredential.Name : ""
            })
            .ToListAsync(ct);
        var byId = devices.ToDictionary(d => d.Id);
        var nameCount = devices.GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        string ParentRef(Guid? id) => id is { } p && byId.TryGetValue(p, out var parent)
            ? nameCount[parent.Name] == 1 ? parent.Name : parent.Address
            : "";

        var csv = DeviceCsv.Write(devices.Select(d => (IReadOnlyList<string>)
        [
            d.Name, d.Address, DeviceCsv.TypeText(d.Type), DeviceCsv.SnmpText(d.SnmpVersion), d.Customer,
            ParentRef(d.ParentDeviceId), d.Profile, d.Enabled ? "si" : "no"
        ]));
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(); // BOM: Excel riconosce l'UTF-8
        return TypedResults.File(bytes, "text/csv; charset=utf-8", $"vedettavip-dispositivi-{time.GetLocalNow():yyyyMMdd}.csv");
    }

    private static DeviceImportResultDto Empty(bool dryRun, string error) =>
        new(dryRun, 0, 0, 0, 0, 0, [], [], [], error);
}
