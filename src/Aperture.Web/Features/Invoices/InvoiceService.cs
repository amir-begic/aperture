using Aperture.Web.Data;
using Aperture.Web.Data.Entities;
using Aperture.Web.Domain;
using Aperture.Web.Features.Attachments;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;

namespace Aperture.Web.Features.Invoices;

/// <summary>An invoice line as edited in a draft.</summary>
public sealed record DraftLine(string Description, decimal Quantity, Money UnitPrice);

/// <summary>Creating, editing, issuing and cancelling invoices. Issued invoices never change.</summary>
public sealed class InvoiceService(IDbContextFactory<ApertureDbContext> dbFactory, AttachmentStore store, TimeProvider time)
{
    private DateOnly Today => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    /// <summary>A draft pre-filled with the gig at its quote and each cost billed to the client.</summary>
    public async Task<int> CreateDraftAsync(int gigId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Invoices.AnyAsync(i => i.GigId == gigId && i.Status != InvoiceStatus.Cancelled))
        {
            throw new InvalidOperationException("This gig already has an invoice. Cancel it first to create a new one.");
        }

        var gig = await db.Gigs.SingleAsync(g => g.Id == gigId);
        var billedCosts = await db.Expenses
            .Where(e => e.GigId == gigId && e.BilledToClient != null)
            .OrderBy(e => e.Date).ThenBy(e => e.Id)
            .ToListAsync();
        var settings = await db.BusinessSettings.SingleAsync();

        var invoice = new Invoice
        {
            GigId = gigId,
            IssueDate = Today,
            DueDate = Today.AddDays(settings.PaymentTermsDays),
        };
        invoice.Lines.Add(new InvoiceLine
        {
            Description = $"DJ performance: {gig.Title}",
            UnitPrice = gig.QuotedPrice ?? Money.Zero,
        });
        invoice.Lines.AddRange(billedCosts.Select(cost => new InvoiceLine
        {
            Description = cost.Description,
            UnitPrice = cost.BilledToClient!.Value,
        }));
        for (var i = 0; i < invoice.Lines.Count; i++) invoice.Lines[i].SortOrder = i;

        db.Invoices.Add(invoice);
        await db.SaveChangesAsync();
        return invoice.Id;
    }

    public async Task SaveDraftAsync(int invoiceId, DateOnly issueDate, DateOnly dueDate, IReadOnlyList<DraftLine> lines)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var invoice = await db.Invoices.Include(i => i.Lines).SingleAsync(i => i.Id == invoiceId);
        EnsureDraft(invoice);

        invoice.IssueDate = issueDate;
        invoice.DueDate = dueDate;
        db.InvoiceLines.RemoveRange(invoice.Lines);
        invoice.Lines = lines.Select((line, index) => new InvoiceLine
        {
            SortOrder = index,
            Description = line.Description.Trim(),
            Quantity = line.Quantity,
            UnitPrice = line.UnitPrice,
        }).ToList();

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The invoice as it will look: issued invoices as stored; drafts with current business, client
    /// and payment data filled in (not saved).
    /// </summary>
    public async Task<(Invoice Invoice, InvoiceGigInfo Gig)> LoadForDocumentAsync(int invoiceId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var invoice = await db.Invoices.AsNoTracking()
            .Include(i => i.Lines)
            .Include(i => i.Gig).ThenInclude(g => g.Client)
            .Include(i => i.Gig).ThenInclude(g => g.Venue)
            .SingleAsync(i => i.Id == invoiceId);

        if (invoice.Status == InvoiceStatus.Draft)
        {
            InvoiceSnapshot.Apply(invoice, await db.BusinessSettings.AsNoTracking().SingleAsync(), invoice.Gig.Client);
            invoice.AlreadyPaid = await ReceivedAsync(db, invoice.GigId);
        }

        var gig = invoice.Gig;
        var venue = gig.Venue is null ? null : gig.Venue.City is null ? gig.Venue.Name : $"{gig.Venue.Name}, {gig.Venue.City}";
        return (invoice, new InvoiceGigInfo(gig.Title, gig.Start, gig.End, venue));
    }

    /// <summary>What has to be fixed before the invoice can be issued; empty when it's ready.</summary>
    public async Task<IReadOnlyList<string>> CheckAsync(int invoiceId)
    {
        var (invoice, _) = await LoadForDocumentAsync(invoiceId);
        await using var db = await dbFactory.CreateDbContextAsync();
        var settings = await db.BusinessSettings.AsNoTracking().SingleAsync();

        var problems = new List<string>();
        if (!settings.IsComplete)
        {
            problems.Add("Your business details are incomplete (Settings).");
        }
        if (!invoice.Client.HasPostalAddress)
        {
            problems.Add($"The billing address of {invoice.Client.Name} is missing.");
        }
        if (invoice.Lines.Count == 0 || invoice.Lines.Any(l => string.IsNullOrWhiteSpace(l.Description)))
        {
            problems.Add("Every invoice line needs a description.");
        }
        if (invoice.Totals.Total <= Money.Zero)
        {
            problems.Add("The invoice total must be above zero.");
        }
        if (invoice.DueDate < invoice.IssueDate)
        {
            problems.Add("The due date is before the invoice date.");
        }
        if (settings.IsComplete)
        {
            problems.AddRange(InvoiceQrBill.Problems(invoice));
        }

        return problems;
    }

    /// <summary>
    /// Numbers the invoice, freezes business and client data, records payments received so far
    /// and archives the PDF in the gig's files. Returns the problems instead if it isn't ready.
    /// </summary>
    public async Task<IReadOnlyList<string>> IssueAsync(int invoiceId)
    {
        var problems = await CheckAsync(invoiceId);
        if (problems.Count > 0)
        {
            return problems;
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var invoice = await db.Invoices.Include(i => i.Lines).Include(i => i.Gig).ThenInclude(g => g.Client)
            .SingleAsync(i => i.Id == invoiceId);
        EnsureDraft(invoice);

        InvoiceSnapshot.Apply(invoice, await db.BusinessSettings.SingleAsync(), invoice.Gig.Client);
        invoice.AlreadyPaid = await ReceivedAsync(db, invoice.GigId);
        invoice.Number = await NextNumberAsync(db, invoice.IssueDate.Year);
        invoice.Status = InvoiceStatus.Issued;
        invoice.IssuedAt = time.GetLocalNow().DateTime;

        var gig = invoice.Gig;
        var venue = await db.Venues.Where(v => v.Id == gig.VenueId)
            .Select(v => v.City == null ? v.Name : v.Name + ", " + v.City).SingleOrDefaultAsync();
        var pdf = new InvoiceDocument(invoice, new InvoiceGigInfo(gig.Title, gig.Start, gig.End, venue)).GeneratePdf();
        var attachment = await store.SaveAsync(new MemoryStream(pdf), $"Invoice {invoice.Number}.pdf", invoice.GigId);
        invoice.Pdf = attachment;

        await store.CommitAsync(async () =>
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }, added: attachment, removed: null);

        return [];
    }

    public async Task CancelAsync(int invoiceId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var invoice = await db.Invoices.SingleAsync(i => i.Id == invoiceId);
        if (invoice.Status != InvoiceStatus.Issued)
        {
            throw new InvalidOperationException("Only issued invoices can be cancelled.");
        }

        invoice.Status = InvoiceStatus.Cancelled;
        invoice.CancelledAt = time.GetLocalNow().DateTime;
        await db.SaveChangesAsync();
    }

    public async Task DeleteDraftAsync(int invoiceId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var invoice = await db.Invoices.SingleAsync(i => i.Id == invoiceId);
        EnsureDraft(invoice);
        db.Invoices.Remove(invoice);
        await db.SaveChangesAsync();
    }

    /// <summary>"2026-001", "2026-002", … per year of the invoice date.</summary>
    public static async Task<string> NextNumberAsync(ApertureDbContext db, int year)
    {
        var prefix = $"{year}-";
        var numbers = await db.Invoices
            .Where(i => i.Number != null && i.Number.StartsWith(prefix))
            .Select(i => i.Number!)
            .ToListAsync();
        var last = numbers.Select(n => int.TryParse(n[prefix.Length..], out var sequence) ? sequence : 0).DefaultIfEmpty(0).Max();
        return $"{prefix}{last + 1:000}";
    }

    private static async Task<Money> ReceivedAsync(ApertureDbContext db, int gigId) =>
        (await db.Payments.Where(p => p.GigId == gigId).Select(p => p.Amount).ToListAsync()).Sum();

    private static void EnsureDraft(Invoice invoice)
    {
        if (invoice.Status != InvoiceStatus.Draft)
        {
            throw new InvalidOperationException($"Invoice {invoice.Number} is issued and can no longer be changed.");
        }
    }
}
