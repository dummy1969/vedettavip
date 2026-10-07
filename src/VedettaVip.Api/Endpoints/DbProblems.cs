// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace VedettaVip.Api.Endpoints;

/// <summary>
/// Traduce le violazioni di vincolo PostgreSQL in risposte HTTP. I controlli applicativi
/// coprono i casi normali; questo copre le race condition tra richieste concorrenti.
/// </summary>
internal static class DbProblems
{
    public static async Task<ProblemHttpResult?> TrySaveChangesAsync(this DbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: var state } pg
                                           && state is PostgresErrorCodes.UniqueViolation
                                               or PostgresErrorCodes.ForeignKeyViolation
                                               or PostgresErrorCodes.CheckViolation)
        {
            return state switch
            {
                PostgresErrorCodes.UniqueViolation => Problem(StatusCodes.Status409Conflict, "Elemento duplicato", pg.ConstraintName),
                PostgresErrorCodes.ForeignKeyViolation => Problem(StatusCodes.Status409Conflict, "Riferimento non valido o ancora in uso", pg.ConstraintName),
                _ => Problem(StatusCodes.Status400BadRequest, "Dati incoerenti", pg.ConstraintName)
            };
        }
    }

    public static ProblemHttpResult Problem(int statusCode, string title, string? detail = null) =>
        TypedResults.Problem(title: title, detail: detail, statusCode: statusCode);
}
