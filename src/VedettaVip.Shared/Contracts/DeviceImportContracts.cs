// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.ComponentModel.DataAnnotations;

namespace VedettaVip.Shared.Contracts;

public enum ImportDuplicateMode
{
    /// <summary>Indirizzo già presente: riga saltata.</summary>
    Skip,
    /// <summary>Indirizzo già presente: il device esistente prende i valori delle colonne presenti nel file.</summary>
    Update
}

public enum ImportRowAction { Create, Update, Skip, Error }

/// <summary>
/// Import di dispositivi da CSV (separatore ; , o tab, intestazione obbligatoria). Con <see cref="DryRun"/> nessuna
/// scrittura: si ottiene l'anteprima riga per riga. Senza, le righe valide vengono importate in un'unica transazione.
/// </summary>
/// <param name="MapId">Mappa su cui aggiungere i dispositivi importati che non vi sono già (disposti a griglia).</param>
/// <param name="CreateMissingCustomers">Crea i clienti non ancora presenti (solo Admin); altrimenti la riga è in errore.</param>
public sealed record DeviceImportRequestDto(
    [property: Required, MaxLength(DeviceImportRequestDto.MaxCsvLength, ErrorMessage = "File troppo grande (massimo 2 MB).")] string Csv,
    bool DryRun = true,
    ImportDuplicateMode OnDuplicate = ImportDuplicateMode.Skip,
    Guid? MapId = null,
    bool CreateMissingCustomers = false)
{
    public const int MaxCsvLength = 2 * 1024 * 1024;
    public const int MaxRows = 2000;
}

public sealed record DeviceImportRowDto(int Line, string Name, string Address, ImportRowAction Action, string? Message);

/// <param name="Columns">Colonne riconosciute (nome canonico); <paramref name="IgnoredColumns"/> quelle sconosciute.</param>
/// <param name="Error">Errore che blocca tutto il file (intestazione mancante, troppe righe...).</param>
public sealed record DeviceImportResultDto(
    bool DryRun,
    int Created,
    int Updated,
    int Skipped,
    int Errors,
    int AddedToMap,
    IReadOnlyList<string> Columns,
    IReadOnlyList<string> IgnoredColumns,
    IReadOnlyList<DeviceImportRowDto> Rows,
    string? Error = null);
