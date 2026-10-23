using System.IO.Compression;
using System.Text;
using ClosedXML.Excel;
using Aperture.Web.Configuration;
using Aperture.Web.Data;
using Aperture.Web.Data.Entities;
using Aperture.Web.Domain;
using Aperture.Web.Features.Attachments;
using Aperture.Web.Features.Finance;
using Aperture.Web.Features.Invoices;
using Aperture.Web.Localization;
using Microsoft.EntityFrameworkCore;

namespace Aperture.Tests.Features;

public sealed class TaxExportTests : IAsyncLifetime
{
    private readonly TestDatabase _database = new();
    private readonly string _dataDirectory = Directory.CreateTempSubdirectory("aperture-tests-").FullName;
    private AttachmentStore _store = null!;
    private TaxExport _export = null!;

    static TaxExportTests() => QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        var time = new FixedTimeProvider(new DateTime(2026, 10, 6));
        _store = new AttachmentStore(new AperturePaths(_dataDirectory), time);
        var factory = new Factory(_database);
        _export = new TaxExport(factory, _store, time);

        await using var db = _database.CreateContext();
        var settings = await db.BusinessSettings.SingleAsync();
        (settings.OwnerName, settings.BusinessName, settings.Street, settings.PostalCode, settings.City, settings.Iban) =
            ("Max Muster", "Studio Aperture", "Musterstrasse 1", "8000", "Zürich", "CH9300762011623852957");

        var client = new Client { Name = "Anna & Marco Rossi", Street = "Seestrasse 5", PostalCode = "8800", City = "Thalwil" };
        var shoot = new Shoot
        {
            Title = "Wedding Rossi", Client = client, Status = ShootStatus.Shot, QuotedPrice = Money.FromChf(1800),
            Start = new DateTime(2026, 6, 13, 20, 0, 0), End = new DateTime(2026, 6, 14, 2, 0, 0),
        };
        shoot.Payments.Add(new Payment { Date = new DateOnly(2026, 6, 20), Amount = Money.FromChf(1800), Proof = await FileAsync("twint.png") });
        shoot.Expenses.Add(new Expense { Date = new DateOnly(2026, 6, 13), Category = ExpenseCategory.GearRental, Description = "Light/set rental",
            Vendor = "Showtech AG", Amount = Money.FromChf(350), Receipt = await FileAsync("Rechnung.pdf") });
        db.Shoots.Add(shoot);
        db.Expenses.Add(new Expense { Date = new DateOnly(2026, 1, 5), Category = ExpenseCategory.Software,
            Description = "Rekordbox", Amount = Money.FromChf(120) });
        db.Assets.Add(new Asset { Name = "Camera", PurchaseDate = new DateOnly(2026, 1, 10), Cost = Money.FromChf(2000),
            DepreciationRatePercent = 30, Receipt = await FileAsync("camera.pdf") });
        await db.SaveChangesAsync();

        var invoices = new InvoiceService(factory, _store, time);
        var invoiceId = await invoices.CreateDraftAsync(shoot.Id, Language.English);
        Assert.Empty(await invoices.IssueAsync(invoiceId));
    }

    private async Task<Attachment> FileAsync(string name) =>
        await _store.SaveAsync(new MemoryStream(Encoding.UTF8.GetBytes($"content of {name}")), name, shootId: null);

    public async Task DisposeAsync()
    {
        await _database.DisposeAsync();
        Directory.Delete(_dataDirectory, recursive: true);
    }

    [Fact]
    public async Task Documents_are_numbered_and_referenced()
    {
        var data = await _export.LoadAsync(2026);

        Assert.Equal(
        [
            "Receipts/R001 2026-06-20 Payment - Wedding Rossi.png",
            "Receipts/R002 2026-06-13 Cost - Light set rental.pdf",   // "/" removed from the name
            "Receipts/R003 2026-01-10 Equipment - Camera.pdf",
            "Invoices/Invoice 2026-001.pdf",
        ], data.Documents.All.Select(d => d.ZipPath));
        Assert.Equal("R002", data.Documents.NumberOf(data.Statement.Expenses.Single(e => e.Receipt is not null).Receipt));
    }

    [Fact]
    public async Task Zip_contains_statement_spreadsheet_and_all_files()
    {
        await using var zipStream = await _export.CreateZipAsync(await _export.LoadAsync(2026));
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);

        Assert.Equal(
        [
            "Aperture 2026/Accounts 2026.xlsx",
            "Aperture 2026/Income statement 2026.pdf",
            "Aperture 2026/Invoices/Invoice 2026-001.pdf",
            "Aperture 2026/Receipts/R001 2026-06-20 Payment - Wedding Rossi.png",
            "Aperture 2026/Receipts/R002 2026-06-13 Cost - Light set rental.pdf",
            "Aperture 2026/Receipts/R003 2026-01-10 Equipment - Camera.pdf",
        ], zip.Entries.Select(e => e.FullName).Order());

        using var reader = new StreamReader(zip.GetEntry("Aperture 2026/Receipts/R002 2026-06-13 Cost - Light set rental.pdf")!.Open());
        Assert.Equal("content of Rechnung.pdf", await reader.ReadToEndAsync());

        using var pdf = new MemoryStream();
        await zip.GetEntry("Aperture 2026/Income statement 2026.pdf")!.Open().CopyToAsync(pdf);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf.ToArray(), 0, 4));
    }

    [Fact]
    public async Task Missing_files_are_listed_instead_of_failing()
    {
        var data = await _export.LoadAsync(2026);
        File.Delete(_store.GetFullPath(data.Documents.All[1].Attachment));

        await using var zipStream = await _export.CreateZipAsync(data);
        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);

        using var note = new StreamReader(zip.GetEntry("Aperture 2026/MISSING FILES.txt")!.Open());
        Assert.Contains("R002: Rechnung.pdf", await note.ReadToEndAsync());
    }

    [Fact]
    public async Task Spreadsheet_has_the_statement_and_all_lines_with_totals()
    {
        var data = await _export.LoadAsync(2026);

        using var workbook = new XLWorkbook(new MemoryStream(TaxExport.BuildSpreadsheet(data)));

        Assert.Equal(["Summary", "Income", "Costs", "Equipment"], workbook.Worksheets.Select(w => w.Name));
        var summary = workbook.Worksheet("Summary");
        var profitRow = summary.RowsUsed().Single(r => r.Cell(1).GetString() == "Profit");
        Assert.Equal(1800m - 350 - 120 - 600, profitRow.Cell(2).GetValue<decimal>());

        var costs = workbook.Worksheet("Costs");
        Assert.Equal("R002", costs.Cell(3, 6).GetString());           // sorted by date: Rekordbox, then lights
        Assert.Equal(new DateTime(2026, 1, 5), costs.Cell(2, 1).GetDateTime());
        var total = costs.Cell(4, 7);
        Assert.Equal("SUM(G2:G3)", total.FormulaA1);
        Assert.Equal(470m, total.GetValue<decimal>());

        Assert.Equal(600m, workbook.Worksheet("Equipment").Cell(2, 7).GetValue<decimal>());
    }

    private sealed class Factory(TestDatabase database) : IDbContextFactory<ApertureDbContext>
    {
        public ApertureDbContext CreateDbContext() => database.CreateContext();
    }
}
