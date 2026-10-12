// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Api.Services;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Tests;

/// <summary>CSV degli export (eventi, interruzioni, dispositivi) e testi condivisi con le pagine.</summary>
public class CsvWriterTests
{
    [Fact]
    public void Writes_header_and_rows_with_semicolons_and_crlf()
    {
        var csv = CsvWriter.Write(["nome", "valore"], [["a", "1"], ["b", ""]]);
        Assert.Equal("nome;valore\r\na;1\r\nb;\r\n", csv);
    }

    [Theory]
    [InlineData("router; sede", "\"router; sede\"")]
    [InlineData("dice \"ciao\"", "\"dice \"\"ciao\"\"\"")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("riga1\nriga2", "\"riga1\nriga2\"")]
    [InlineData(" spazio", "\" spazio\"")]
    public void Quotes_cells_that_would_break_the_columns(string value, string expected) =>
        Assert.Equal($"x\r\n{expected}\r\n", CsvWriter.Write(["x"], [[value]]));

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+39 333", "'+39 333")]
    [InlineData("-cmd", "'-cmd")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("Router (10.0.0.1): Up → Down", "Router (10.0.0.1): Up → Down")]
    [InlineData("", "")]
    public void Protects_cells_that_excel_would_run_as_formulas(string value, string expected) =>
        Assert.Equal($"x\r\n{expected}\r\n", CsvWriter.Write(["x"], [[value]], protectFormulas: true));

    [Fact]
    public void Device_export_is_left_unprotected_so_it_imports_back_unchanged() =>
        Assert.Equal("x\r\n-nome\r\n", CsvWriter.Write(["x"], [["-nome"]]));

    [Fact]
    public void Times_are_written_in_the_given_zone_and_empty_when_missing()
    {
        var rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");
        Assert.Equal("2026-10-12 14:03:27", CsvWriter.Time(new DateTimeOffset(2026, 10, 12, 12, 3, 27, TimeSpan.Zero), rome)); // ora legale
        Assert.Equal("2026-12-01 09:00:00", CsvWriter.Time(new DateTimeOffset(2026, 12, 1, 8, 0, 0, TimeSpan.Zero), rome));
        Assert.Equal("", CsvWriter.Time(null, rome));
    }

    [Fact]
    public void Utf8_bom_is_added_to_the_file()
    {
        var file = CsvWriter.File("x\r\n", "prova.csv");
        Assert.Equal([0xEF, 0xBB, 0xBF, (byte)'x', 13, 10], file.FileContents.ToArray());
        Assert.Equal("prova.csv", file.FileDownloadName);
    }

    [Theory]
    [InlineData(200, "00:03:20")]
    [InlineData(0, "00:00:00")]
    [InlineData(-5, "00:00:00")]
    [InlineData(10 * 86400 + 12 * 3600 + 4 * 60 + 38, "10g 12:04:38")]
    public void Duration_is_written_like_the_dude(int seconds, string expected) =>
        Assert.Equal(expected, Labels.Duration(TimeSpan.FromSeconds(seconds)));
}
