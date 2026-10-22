using Codecrete.SwissQRBill.Generator;
using Aperture.Web.Configuration;
using Aperture.Web.Data;
using Aperture.Web.Data.Entities;
using Aperture.Web.Domain;
using Aperture.Web.Features.Attachments;
using Aperture.Web.Features.Invoices;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;

namespace Aperture.Tests.Features;

public sealed class InvoiceServiceTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 6, 10, 0, 0);
    private readonly TestDatabase _database = new();
    private readonly string _dataDirectory = Directory.CreateTempSubdirectory("aperture-tests-").FullName;
    private AttachmentStore _store = null!;
    private InvoiceService _invoices = null!;

    static InvoiceServiceTests() => QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        var time = new FixedTimeProvider(Now);
        _store = new AttachmentStore(new AperturePaths(_dataDirectory), time);
        _invoices = new InvoiceService(new Factory(_database), _store, time);

        await using var db = _database.CreateContext();
        var settings = await db.BusinessSettings.SingleAsync();
        settings.OwnerName = "Max Muster";
        settings.BusinessName = "DJ Aperture";
        settings.Street = "Musterstrasse 1";
        settings.PostalCode = "8000";
        settings.City = "Zürich";
        settings.Iban = "CH9300762011623852957";
        settings.PaymentTermsDays = 30;
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _database.DisposeAsync();
        Directory.Delete(_dataDirectory, recursive: true);
    }

    /// <summary>A gig quoted at 1800 with lights billed at 450 on top and a 500 deposit paid.</summary>
    private async Task<Gig> AddWeddingAsync(bool withAddress = true)
    {
        await using var db = _database.CreateContext();
        var client = new Client
        {
            Name = "Anna & Marco Rossi",
            Street = withAddress ? "Seestrasse 5" : null,
            PostalCode = withAddress ? "8800" : null,
            City = withAddress ? "Thalwil" : null,
        };
        var gig = new Gig
        {
            Title = "Wedding Rossi",
            Client = client,
            Status = GigStatus.Confirmed,
            Start = new DateTime(2026, 12, 12, 20, 0, 0),
            End = new DateTime(2026, 12, 13, 2, 0, 0),
            QuotedPrice = Money.FromChf(1800),
        };
        gig.Expenses.Add(new Expense { Description = "Light set rental", Date = new DateOnly(2026, 12, 12),
            Amount = Money.FromChf(350), BilledToClient = Money.FromChf(450) });
        gig.Expenses.Add(new Expense { Description = "PA top-up", Date = new DateOnly(2026, 12, 12), Amount = Money.FromChf(200) });
        gig.Payments.Add(new Payment { Date = new DateOnly(2026, 9, 1), Amount = Money.FromChf(500), Note = "Deposit" });
        db.Gigs.Add(gig);
        await db.SaveChangesAsync();
        return gig;
    }

    [Fact]
    public async Task Draft_is_prefilled_with_the_quote_and_billed_costs()
    {
        var gig = await AddWeddingAsync();

        var id = await _invoices.CreateDraftAsync(gig.Id);

        var (invoice, _) = await _invoices.LoadForDocumentAsync(id);
        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.Null(invoice.Number);
        Assert.Equal(["DJ performance: Wedding Rossi", "Light set rental"], invoice.Lines.OrderBy(l => l.SortOrder).Select(l => l.Description));
        Assert.Equal(new DateOnly(2026, 11, 5), invoice.DueDate);   // 30 days payment terms
        Assert.Equal(Money.FromChf(2250), invoice.Totals.Total);
        Assert.Equal(Money.FromChf(1750), invoice.Totals.AmountDue); // deposit already paid
    }

    [Fact]
    public async Task Only_one_active_invoice_per_gig()
    {
        var gig = await AddWeddingAsync();
        await _invoices.CreateDraftAsync(gig.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _invoices.CreateDraftAsync(gig.Id));
    }

    [Fact]
    public async Task Draft_lines_can_be_edited()
    {
        var gig = await AddWeddingAsync();
        var id = await _invoices.CreateDraftAsync(gig.Id);

        await _invoices.SaveDraftAsync(id, new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 20),
        [
            new("DJ performance, 20:00–02:00", 1, Money.FromChf(1800)),
            new("Lighting", 1, Money.FromChf(450)),
            new("Extra hour", 1.5m, Money.FromChf(150)),
        ]);

        var (invoice, _) = await _invoices.LoadForDocumentAsync(id);
        Assert.Equal("Lighting", invoice.Lines.Single(l => l.SortOrder == 1).Description);
        Assert.Equal(Money.FromChf(2475), invoice.Totals.Total);
        Assert.Equal(new DateOnly(2026, 10, 20), invoice.DueDate);
    }

    [Fact]
    public async Task Issuing_numbers_freezes_and_archives_the_invoice()
    {
        var gig = await AddWeddingAsync();
        var id = await _invoices.CreateDraftAsync(gig.Id);

        Assert.Empty(await _invoices.IssueAsync(id));

        await using var db = _database.CreateContext();
        var invoice = await db.Invoices.Include(i => i.Pdf).Include(i => i.Lines).SingleAsync(i => i.Id == id);
        Assert.Equal(InvoiceStatus.Issued, invoice.Status);
        Assert.Equal("2026-001", invoice.Number);
        Assert.Equal("DJ Aperture", invoice.Seller.Company);
        Assert.Equal("Thalwil", invoice.Client.City);
        Assert.Equal(Money.FromChf(500), invoice.AlreadyPaid);
        Assert.Equal("Invoice 2026-001.pdf", invoice.Pdf!.FileName);
        Assert.Equal(gig.Id, invoice.Pdf.GigId);
        var pdf = await File.ReadAllBytesAsync(_store.GetFullPath(invoice.Pdf));
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));

        // Later changes to settings or client don't touch the issued invoice.
        (await db.BusinessSettings.SingleAsync()).City = "Bern";
        (await db.Clients.SingleAsync()).City = "Basel";
        await db.SaveChangesAsync();
        var (reloaded, _) = await _invoices.LoadForDocumentAsync(id);
        Assert.Equal("Zürich", reloaded.Seller.City);
        Assert.Equal("Thalwil", reloaded.Client.City);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _invoices.SaveDraftAsync(id, invoice.IssueDate, invoice.DueDate, [new("Changed", 1, Money.FromChf(1))]));
    }

    [Fact]
    public async Task Numbers_are_sequential_and_survive_cancellation()
    {
        var first = await AddWeddingAsync();
        var second = await AddWeddingAsync();

        var firstId = await _invoices.CreateDraftAsync(first.Id);
        await _invoices.IssueAsync(firstId);
        await _invoices.CancelAsync(firstId);
        var replacementId = await _invoices.CreateDraftAsync(first.Id); // allowed after cancelling
        await _invoices.IssueAsync(replacementId);
        var secondId = await _invoices.CreateDraftAsync(second.Id);
        await _invoices.IssueAsync(secondId);

        await using var db = _database.CreateContext();
        var numbers = await db.Invoices.OrderBy(i => i.Id).Select(i => new { i.Number, i.Status }).ToListAsync();
        Assert.Equal(["2026-001", "2026-002", "2026-003"], numbers.Select(n => n.Number));
        Assert.Equal(InvoiceStatus.Cancelled, numbers[0].Status);
    }

    [Fact]
    public async Task Missing_billing_address_blocks_issuing()
    {
        var gig = await AddWeddingAsync(withAddress: false);
        var id = await _invoices.CreateDraftAsync(gig.Id);

        var problems = await _invoices.IssueAsync(id);

        Assert.Contains(problems, p => p.Contains("billing address"));
        await using var db = _database.CreateContext();
        Assert.Equal(InvoiceStatus.Draft, (await db.Invoices.SingleAsync()).Status);
    }

    [Fact]
    public async Task Qr_iban_is_rejected_with_an_explanation()
    {
        var gig = await AddWeddingAsync();
        await using (var db = _database.CreateContext())
        {
            (await db.BusinessSettings.SingleAsync()).Iban = "CH4431999123000889012"; // QR-IBAN (IID 31999)
            await db.SaveChangesAsync();
        }

        var problems = await _invoices.CheckAsync(await _invoices.CreateDraftAsync(gig.Id));

        Assert.Contains(problems, p => p.Contains("QR-IBAN"));
    }

    [Fact]
    public async Task Qr_bill_carries_amount_due_iban_and_rf_reference()
    {
        var gig = await AddWeddingAsync();
        var id = await _invoices.CreateDraftAsync(gig.Id);
        await _invoices.IssueAsync(id);
        var (invoice, _) = await _invoices.LoadForDocumentAsync(id);

        var bill = InvoiceQrBill.Create(invoice);
        var decoded = QRBill.DecodeQrCodeText(QRBill.EncodeQrCodeText(bill));

        Assert.True(QRBill.Validate(bill).IsValid);
        Assert.Equal(1750m, decoded.Amount);
        Assert.Equal("CHF", decoded.Currency);
        Assert.Equal("CH9300762011623852957", decoded.Account);
        Assert.StartsWith("RF", decoded.Reference);
        Assert.True(Payments.IsValidIso11649Reference(decoded.Reference));
        Assert.Equal("Invoice 2026-001", decoded.UnstructuredMessage);
        Assert.Equal("Anna & Marco Rossi", decoded.Debtor.Name);
    }

    [Fact]
    public async Task Paid_in_full_invoices_render_without_a_qr_bill()
    {
        var gig = await AddWeddingAsync();
        await _database.AddPaymentAsync(gig, 1750);
        var id = await _invoices.CreateDraftAsync(gig.Id);
        var (invoice, info) = await _invoices.LoadForDocumentAsync(id);

        Assert.Equal(Money.Zero, invoice.Totals.AmountDue);
        var pdf = new InvoiceDocument(invoice, info).GeneratePdf();
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    private sealed class Factory(TestDatabase database) : IDbContextFactory<ApertureDbContext>
    {
        public ApertureDbContext CreateDbContext() => database.CreateContext();
    }
}
