using System.IO.Compression;
using ClosedXML.Excel;
using Aperture.Web.Data;
using Aperture.Web.Data.Entities;
using Aperture.Web.Domain;
using Aperture.Web.Features.Attachments;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;

namespace Aperture.Web.Features.Finance;

/// <summary>Everything the yearly export needs, loaded once.</summary>
public sealed record TaxExportData(
    IncomeStatement Statement, BusinessSettings Settings, IReadOnlyList<Invoice> Invoices, TaxDocuments Documents, DateOnly CreatedOn);

/// <summary>
/// The yearly package for the tax return or a fiduciary: income statement as PDF, all lines as a spreadsheet,
/// and every receipt, payslip and invoice of the year in a ZIP, numbered to match.
/// </summary>
public sealed class TaxExport(IDbContextFactory<ApertureDbContext> dbFactory, AttachmentStore store, TimeProvider time)
{
    public async Task<TaxExportData> LoadAsync(int year)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var statement = await IncomeStatement.LoadAsync(db, year);
        var settings = await db.BusinessSettings.AsNoTracking().SingleAsync();
        var invoices = await db.Invoices.AsNoTracking()
            .Include(i => i.Pdf)
            .Where(i => i.Status != InvoiceStatus.Draft && i.IssueDate.Year == year)
            .ToListAsync();

        return new TaxExportData(statement, settings, invoices, TaxDocuments.For(statement, invoices),
            DateOnly.FromDateTime(time.GetLocalNow().DateTime));
    }

    public static string BaseName(int year) => $"Income statement {year}";

    public static byte[] BuildPdf(TaxExportData data) => new TaxStatementDocument(data).GeneratePdf();

    public static byte[] BuildSpreadsheet(TaxExportData data)
    {
        var statement = data.Statement;
        using var workbook = new XLWorkbook();

        var summary = workbook.Worksheets.Add("Summary");
        summary.Cell(1, 1).Value = $"Income statement {statement.Year} (cash basis, CHF)";
        summary.Cell(1, 1).Style.Font.Bold = true;
        var summaryRows = new List<(string Label, Money Amount, bool Bold)> { ("Income from DJ services", statement.TotalIncome, true) };
        summaryRows.AddRange(statement.CostsByCategory.Select(c => (c.Category.Text(), -c.Amount, false)));
        if (statement.TotalDepreciation != Money.Zero) summaryRows.Add(("Depreciation of equipment", -statement.TotalDepreciation, false));
        if (statement.DisposalResult != Money.Zero) summaryRows.Add(("Result from sale of equipment", statement.DisposalResult, false));
        summaryRows.Add((statement.Result >= Money.Zero ? "Profit" : "Loss", statement.Result, true));
        for (var i = 0; i < summaryRows.Count; i++)
        {
            var (label, amount, bold) = summaryRows[i];
            summary.Cell(i + 3, 1).Value = label;
            Amount(summary.Cell(i + 3, 2), amount);
            summary.Row(i + 3).Style.Font.Bold = bold;
        }
        summary.Column(1).Width = 40;
        summary.Column(2).Width = 16;

        Sheet(workbook, "Income", ["Date", "Gig", "Client", "Invoice", "Method", "Note", "Doc.", "Amount"],
            statement.Income.Select(i => new object?[]
            {
                i.Date, i.GigTitle, i.ClientName, i.InvoiceNumber, i.Method.ToString(), i.Note, data.Documents.NumberOf(i.Proof), i.Amount,
            }),
            widths: [12, 30, 25, 12, 14, 20, 8, 14], totals: ["Amount"]);

        Sheet(workbook, "Costs", ["Date", "Category", "Description", "Vendor", "Gig", "Doc.", "Amount"],
            statement.Expenses.Select(e => new object?[]
            {
                e.Date, e.Category.Text(), e.Description, e.Vendor, e.GigTitle, data.Documents.NumberOf(e.Receipt), e.Amount,
            }),
            widths: [12, 26, 34, 20, 25, 8, 14], totals: ["Amount"]);

        Sheet(workbook, "Equipment", ["Equipment", "Category", "Bought", "Price", "Rate %", "Start", "Depreciation", "End", "Sold on", "Sale result", "Doc."],
            statement.Assets.Select(a => new object?[]
            {
                a.Asset.Name, a.Asset.Category.Text(), a.Asset.PurchaseDate, a.Asset.Cost, a.Asset.DepreciationRatePercent,
                a.Year.Opening, a.Year.Depreciation, a.Year.Closing, a.Asset.DisposedOn, a.Year.DisposalResult,
                data.Documents.NumberOf(a.Asset.Receipt),
            }),
            widths: [30, 22, 12, 14, 8, 14, 14, 14, 12, 14, 8], totals: ["Depreciation", "Sale result"]);

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    /// <summary>
    /// Writes the ZIP to a temporary file that deletes itself when closed, positioned at the start.
    /// (Built on disk, not in the response: receipts add up, and ZipArchive finishes with synchronous writes.)
    /// </summary>
    public async Task<Stream> CreateZipAsync(TaxExportData data, CancellationToken cancellationToken = default)
    {
        var file = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None,
            bufferSize: 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            await WriteZipAsync(data, file, cancellationToken);
            file.Position = 0;
            return file;
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    /// <summary>Writes the ZIP: PDF, spreadsheet, receipts and invoices. Files missing on disk are listed in a note.</summary>
    public async Task WriteZipAsync(TaxExportData data, Stream output, CancellationToken cancellationToken = default)
    {
        var folder = $"Aperture {data.Statement.Year}";
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        await AddAsync(zip, $"{folder}/{BaseName(data.Statement.Year)}.pdf", BuildPdf(data), cancellationToken);
        await AddAsync(zip, $"{folder}/Accounts {data.Statement.Year}.xlsx", BuildSpreadsheet(data), cancellationToken);

        var missing = new List<string>();
        foreach (var document in data.Documents.All)
        {
            var path = store.GetFullPath(document.Attachment);
            if (!File.Exists(path))
            {
                missing.Add($"{document.Number}: {document.Attachment.FileName}");
                continue;
            }

            var entry = zip.CreateEntry($"{folder}/{document.ZipPath}", CompressionLevel.Optimal);
            await using var target = entry.Open();
            await using var source = File.OpenRead(path);
            await source.CopyToAsync(target, cancellationToken);
        }

        if (missing.Count > 0)
        {
            var note = "These files were not found on the server:\n" + string.Join("\n", missing) + "\n";
            await AddAsync(zip, $"{folder}/MISSING FILES.txt", System.Text.Encoding.UTF8.GetBytes(note), cancellationToken);
        }
    }

    private static async Task AddAsync(ZipArchive zip, string path, byte[] content, CancellationToken cancellationToken)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await stream.WriteAsync(content, cancellationToken);
    }

    private static void Sheet(XLWorkbook workbook, string name, string[] headers, IEnumerable<object?[]> rows, int[] widths, string[] totals)
    {
        var sheet = workbook.Worksheets.Add(name);
        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
            sheet.Column(c + 1).Width = widths[c];
        }
        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);

        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length; c++)
            {
                var cell = sheet.Cell(r, c + 1);
                switch (row[c])
                {
                    case Money money: Amount(cell, money); break;
                    case DateOnly date:
                        cell.Value = date.ToDateTime(TimeOnly.MinValue);
                        cell.Style.DateFormat.Format = "dd.mm.yyyy";
                        break;
                    case decimal number: cell.Value = number; break;
                    case string text: cell.Value = text; break;
                }
            }
            r++;
        }

        // Totals as formulas, so edits in Excel stay consistent.
        sheet.Cell(r, 1).Value = "Total";
        sheet.Row(r).Style.Font.Bold = true;
        foreach (var total in totals)
        {
            var column = sheet.Column(Array.IndexOf(headers, total) + 1).ColumnLetter();
            var cell = sheet.Cell(r, Array.IndexOf(headers, total) + 1);
            cell.FormulaA1 = r > 2 ? $"SUM({column}2:{column}{r - 1})" : "0";
            cell.Style.NumberFormat.Format = AmountFormat;
        }
    }

    private const string AmountFormat = "#,##0.00";

    private static void Amount(IXLCell cell, Money amount)
    {
        cell.Value = amount.Chf;
        cell.Style.NumberFormat.Format = AmountFormat;
    }
}

public static class TaxExportEndpoints
{
    /// <summary>Downloads of the yearly export: /tax/2026/statement.pdf, /tax/2026/accounts.xlsx, /tax/2026/export.zip.</summary>
    public static void MapTaxExportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/tax/{year:int:range(2000,2100)}");

        group.MapGet("/statement.pdf", async (int year, TaxExport export) =>
            Results.File(TaxExport.BuildPdf(await export.LoadAsync(year)), "application/pdf", $"{TaxExport.BaseName(year)}.pdf"));

        group.MapGet("/accounts.xlsx", async (int year, TaxExport export) =>
            Results.File(TaxExport.BuildSpreadsheet(await export.LoadAsync(year)),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Accounts {year}.xlsx"));

        group.MapGet("/export.zip", async (int year, TaxExport export, CancellationToken cancellationToken) =>
        {
            var zip = await export.CreateZipAsync(await export.LoadAsync(year), cancellationToken);
            return Results.File(zip, "application/zip", $"Aperture {year} tax export.zip"); // disposing deletes the temp file
        });
    }
}
