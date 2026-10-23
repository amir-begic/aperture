using Aperture.Web.Configuration;
using Aperture.Web.Data;
using Aperture.Web.Data.Entities;
using Aperture.Web.Domain;
using Aperture.Web.Features.Attachments;
using Aperture.Web.Features.Invoices;
using Aperture.Web.Features.Quotes;
using Aperture.Web.Features.Shoots;
using Aperture.Web.Localization;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;

namespace Aperture.Tests.Features;

public sealed class QuoteServiceTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 6, 10, 0, 0);
    private readonly TestDatabase _database = new();
    private readonly string _dataDirectory = Directory.CreateTempSubdirectory("aperture-tests-").FullName;
    private AttachmentStore _store = null!;
    private QuoteService _quotes = null!;
    private InvoiceService _invoices = null!;

    static QuoteServiceTests() => QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        var time = new FixedTimeProvider(Now);
        _store = new AttachmentStore(new AperturePaths(_dataDirectory), time);
        _quotes = new QuoteService(new Factory(_database), _store, time);
        _invoices = new InvoiceService(new Factory(_database), _store, time);

        await using var db = _database.CreateContext();
        var settings = await db.BusinessSettings.SingleAsync();
        settings.OwnerName = "Max Muster";
        settings.BusinessName = "Studio Aperture";
        settings.Street = "Musterstrasse 1";
        settings.PostalCode = "8000";
        settings.City = "Zürich";
        settings.Iban = "CH9300762011623852957";
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _database.DisposeAsync();
        Directory.Delete(_dataDirectory, recursive: true);
    }

    /// <summary>An inquiry for a wedding, quoted at 3800 from the "Wedding full day" package.</summary>
    private async Task<Shoot> AddInquiryAsync(bool withPackage = true)
    {
        await using var db = _database.CreateContext();
        var shoot = new Shoot
        {
            Title = "Wedding Rossi",
            Client = new Client { Name = "Anna & Marco Rossi", Street = "Seestrasse 5", PostalCode = "8800", City = "Thalwil" },
            Status = ShootStatus.Inquiry,
            ShootType = ShootType.Wedding,
            Start = new DateTime(2027, 6, 12, 12, 0, 0),
            End = new DateTime(2027, 6, 12, 22, 0, 0),
            QuotedPrice = Money.FromChf(3800),
            Package = withPackage
                ? new Package { Name = "Wedding full day", Price = Money.FromChf(3800), Included = "10 hours, 400 edited images" }
                : null,
        };
        db.Shoots.Add(shoot);
        await db.SaveChangesAsync();
        return shoot;
    }

    private async Task<Quote> LoadAsync(int quoteId)
    {
        await using var db = _database.CreateContext();
        return await db.Quotes.Include(q => q.Lines).Include(q => q.Pdf).SingleAsync(q => q.Id == quoteId);
    }

    [Fact]
    public async Task Draft_starts_from_the_package_and_price_and_holds_for_a_month()
    {
        var shoot = await AddInquiryAsync();

        var quote = await LoadAsync(await _quotes.CreateDraftAsync(shoot.Id, Language.English));

        Assert.Equal(QuoteStatus.Draft, quote.Status);
        Assert.Null(quote.Number);
        Assert.Equal(["Wedding full day: Wedding Rossi"], quote.Lines.Select(l => l.Description));
        Assert.Equal(Money.FromChf(3800), quote.Totals.Total);
        Assert.Equal("10 hours, 400 edited images", quote.Included);
        Assert.Equal(new DateOnly(2026, 10, 6), quote.QuoteDate);
        Assert.Equal(new DateOnly(2026, 11, 5), quote.ValidUntil);
    }

    [Fact]
    public async Task Draft_without_a_package_is_photography_in_the_quotes_language()
    {
        var shoot = await AddInquiryAsync(withPackage: false);

        var quote = await LoadAsync(await _quotes.CreateDraftAsync(shoot.Id, Language.German));

        Assert.Equal(Language.German, quote.Language);
        Assert.Equal("Fotografie: Wedding Rossi", quote.Lines.Single().Description);
        Assert.Null(quote.Included);
    }

    [Fact]
    public async Task Only_one_draft_per_shoot()
    {
        var shoot = await AddInquiryAsync();
        await _quotes.CreateDraftAsync(shoot.Id, Language.English);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _quotes.CreateDraftAsync(shoot.Id, Language.English));
    }

    [Fact]
    public async Task Finishing_numbers_freezes_and_archives_the_quote()
    {
        var shoot = await AddInquiryAsync();
        var id = await _quotes.CreateDraftAsync(shoot.Id, Language.German);

        Assert.Empty(await _quotes.SendAsync(id));

        var quote = await LoadAsync(id);
        Assert.Equal(QuoteStatus.Sent, quote.Status);
        Assert.Equal("Q-2026-001", quote.Number);
        Assert.Equal("Studio Aperture", quote.Seller.Company);
        Assert.Equal("Thalwil", quote.Client.City);
        Assert.Equal("Offerte Q-2026-001.pdf", quote.Pdf!.FileName);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(await File.ReadAllBytesAsync(_store.GetFullPath(quote.Pdf)), 0, 4));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _quotes.SaveDraftAsync(id, quote.QuoteDate, quote.ValidUntil, quote.Language, null, [new("Changed", 1, Money.FromChf(1))]));
    }

    [Fact]
    public async Task Incomplete_business_details_block_finishing()
    {
        await using (var db = _database.CreateContext())
        {
            (await db.BusinessSettings.SingleAsync()).OwnerName = "";
            await db.SaveChangesAsync();
        }
        var shoot = await AddInquiryAsync();
        var id = await _quotes.CreateDraftAsync(shoot.Id, Language.English);

        var problems = await _quotes.SendAsync(id);

        Assert.Contains("Your business details are incomplete (Settings).", problems);
        Assert.Equal(QuoteStatus.Draft, (await LoadAsync(id)).Status);
    }

    [Fact]
    public async Task Accepting_books_the_inquiry_at_the_quoted_price()
    {
        var shoot = await AddInquiryAsync();
        var id = await _quotes.CreateDraftAsync(shoot.Id, Language.English);
        await _quotes.SaveDraftAsync(id, new DateOnly(2026, 10, 6), new DateOnly(2026, 11, 5), Language.English, "Everything",
        [
            new("Wedding full day: Wedding Rossi", 1, Money.FromChf(3800)),
            new("Second shooter", 1, Money.FromChf(600)),
        ]);
        await _quotes.SendAsync(id);

        await _quotes.AcceptAsync(id);

        await using var db = _database.CreateContext();
        var booked = await db.Shoots.SingleAsync(s => s.Id == shoot.Id);
        Assert.Equal(ShootStatus.Booked, booked.Status);
        Assert.Equal(Money.FromChf(4400), booked.QuotedPrice);
        Assert.Equal(QuoteStatus.Accepted, (await LoadAsync(id)).Status);
    }

    [Fact]
    public async Task Declining_cancels_an_inquiry()
    {
        var shoot = await AddInquiryAsync();
        var id = await _quotes.CreateDraftAsync(shoot.Id, Language.English);
        await _quotes.SendAsync(id);

        await _quotes.DeclineAsync(id);

        await using var db = _database.CreateContext();
        Assert.Equal(ShootStatus.Cancelled, (await db.Shoots.SingleAsync(s => s.Id == shoot.Id)).Status);
        Assert.Equal(QuoteStatus.Declined, (await LoadAsync(id)).Status);
    }

    [Fact]
    public async Task Only_sent_quotes_can_be_answered()
    {
        var shoot = await AddInquiryAsync();
        var id = await _quotes.CreateDraftAsync(shoot.Id, Language.English);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _quotes.AcceptAsync(id));
    }

    [Fact]
    public async Task A_new_version_copies_the_last_quote_and_gets_the_next_number()
    {
        var shoot = await AddInquiryAsync();
        var first = await _quotes.CreateDraftAsync(shoot.Id, Language.English);
        await _quotes.SaveDraftAsync(first, new DateOnly(2026, 10, 6), new DateOnly(2026, 11, 5), Language.English, "Everything",
            [new("Wedding, 8 hours", 1, Money.FromChf(3200)), new("Album", 1, Money.FromChf(400))]);
        await _quotes.SendAsync(first);

        var second = await _quotes.CreateDraftAsync(shoot.Id, Language.English);
        await _quotes.SendAsync(second);

        var quote = await LoadAsync(second);
        Assert.Equal(["Wedding, 8 hours", "Album"], quote.Lines.OrderBy(l => l.SortOrder).Select(l => l.Description));
        Assert.Equal("Everything", quote.Included);
        Assert.Equal("Q-2026-002", quote.Number);
    }

    [Fact]
    public async Task The_invoice_starts_from_the_accepted_quote_plus_costs_billed_since()
    {
        var shoot = await AddInquiryAsync();
        var id = await _quotes.CreateDraftAsync(shoot.Id, Language.English);
        await _quotes.SaveDraftAsync(id, new DateOnly(2026, 10, 6), new DateOnly(2026, 11, 5), Language.English, null,
            [new("Wedding, 10 hours", 1, Money.FromChf(3800)), new("Second shooter", 1, Money.FromChf(600))]);
        await _quotes.SendAsync(id);
        await _quotes.AcceptAsync(id);
        await using (var db = _database.CreateContext())
        {
            db.Expenses.Add(new Expense { ShootId = shoot.Id, Description = "Light set rental", Date = new DateOnly(2027, 6, 12),
                Amount = Money.FromChf(350), BilledToClient = Money.FromChf(450) });
            await db.SaveChangesAsync();
        }

        var (invoice, _) = await _invoices.LoadForDocumentAsync(await _invoices.CreateDraftAsync(shoot.Id, Language.English));

        Assert.Equal(["Wedding, 10 hours", "Second shooter", "Light set rental"],
            invoice.Lines.OrderBy(l => l.SortOrder).Select(l => l.Description));
        Assert.Equal(Money.FromChf(4850), invoice.Totals.Total);
    }

    [Fact]
    public async Task A_price_changed_after_accepting_wins_over_the_quote()
    {
        var shoot = await AddInquiryAsync();
        var id = await _quotes.CreateDraftAsync(shoot.Id, Language.English);
        await _quotes.SendAsync(id);
        await _quotes.AcceptAsync(id);
        await using (var db = _database.CreateContext())
        {
            (await db.Shoots.SingleAsync(s => s.Id == shoot.Id)).QuotedPrice = Money.FromChf(3500);
            await db.SaveChangesAsync();
        }

        var (invoice, _) = await _invoices.LoadForDocumentAsync(await _invoices.CreateDraftAsync(shoot.Id, Language.English));

        Assert.Equal(Money.FromChf(3500), invoice.Totals.Total);
        Assert.Equal("Wedding full day: Wedding Rossi", invoice.Lines.Single().Description);
    }

    [Fact]
    public async Task Quotes_go_with_their_shoot()
    {
        var shoot = await AddInquiryAsync();
        var id = await _quotes.CreateDraftAsync(shoot.Id, Language.English);
        await _quotes.SendAsync(id);
        var pdf = _store.GetFullPath((await LoadAsync(id)).Pdf!);

        await using var db = _database.CreateContext();
        Assert.False(await db.HasFinancialRecordsAsync(shoot.Id));
        await db.DeleteShootAsync(shoot.Id, _store);

        Assert.False(await db.Quotes.AnyAsync());
        Assert.False(await db.QuoteLines.AnyAsync());
        Assert.False(File.Exists(pdf));
    }

    [Fact]
    public async Task Draft_preview_renders()
    {
        var shoot = await AddInquiryAsync();
        var (quote, info) = await _quotes.LoadForDocumentAsync(await _quotes.CreateDraftAsync(shoot.Id, Language.German));

        var pdf = new QuoteDocument(quote, info).GeneratePdf();

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    private sealed class Factory(TestDatabase database) : IDbContextFactory<ApertureDbContext>
    {
        public ApertureDbContext CreateDbContext() => database.CreateContext();
    }
}
