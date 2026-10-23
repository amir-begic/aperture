using Aperture.Web.Data;
using Aperture.Web.Data.Entities;
using Aperture.Web.Domain;
using Aperture.Web.Features.Attachments;
using Aperture.Web.Localization;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;

namespace Aperture.Web.Features.Invoices;

/// <summary>An invoice line as edited in a draft.</summary>
public sealed record DraftLine(string Description, decimal Quantity, Money UnitPrice);

/// <summary>Creating, editing, issuing and cancelling invoices. Issued invoices never change.</summary>
public sealed class InvoiceService(IDbContextFactory<ApertureDbContext> dbFactory, AttachmentStore store, TimeProvider time)
{
    private DateOnly Today => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    /// <summary>A draft in <paramref name="language"/>, pre-filled with the shoot at its quote and each cost billed to the client.</summary>
    public async Task<int> CreateDraftAsync(int shootId, Language language)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Invoices.AnyAsync(i => i.ShootId == shootId && i.Status != InvoiceStatus.Cancelled))
        {
            throw new InvalidOperationException("This shoot already has an invoice. Cancel it first to create a new one.");
        }

        var shoot = await db.Shoots.Include(s => s.Package).SingleAsync(s => s.Id == shootId);
        var billedCosts = await db.Expenses
            .Where(e => e.ShootId == shootId && e.BilledToClient != null)
            .OrderBy(e => e.Date).ThenBy(e => e.Id)
            .ToListAsync();
        var settings = await db.BusinessSettings.SingleAsync();

        var invoice = new Invoice
        {
            ShootId = shootId,
            IssueDate = Today,
            DueDate = Today.AddDays(settings.PaymentTermsDays),
            Language = language,
        };
        invoice.Lines.Add(new InvoiceLine
        {
            Description = shoot.Package is { } package ? $"{package.Name}: {shoot.Title}" : ShootLine(shoot.Title, language),
            UnitPrice = shoot.QuotedPrice ?? Money.Zero,
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

    /// <summary>"Photography: Wedding Rossi", the first line of a draft for a shoot without a package.</summary>
    public static string ShootLine(string shootTitle, Language language) =>
        string.Format(language.Culture(), Texts.Get("Photography: {0}", language.Culture()), shootTitle);

    public async Task SaveDraftAsync(
        int invoiceId, DateOnly issueDate, DateOnly dueDate, Language language, IReadOnlyList<DraftLine> lines)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var invoice = await db.Invoices.Include(i => i.Lines).SingleAsync(i => i.Id == invoiceId);
        EnsureDraft(invoice);

        invoice.IssueDate = issueDate;
        invoice.DueDate = dueDate;
        invoice.Language = language;
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
    public async Task<(Invoice Invoice, InvoiceShootInfo Shoot)> LoadForDocumentAsync(int invoiceId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var invoice = await db.Invoices.AsNoTracking()
            .Include(i => i.Lines)
            .Include(i => i.Shoot).ThenInclude(s => s.Client)
            .Include(i => i.Shoot).ThenInclude(s => s.Location)
            .SingleAsync(i => i.Id == invoiceId);

        if (invoice.Status == InvoiceStatus.Draft)
        {
            InvoiceSnapshot.Apply(invoice, await db.BusinessSettings.AsNoTracking().SingleAsync(), invoice.Shoot.Client);
            invoice.AlreadyPaid = await ReceivedAsync(db, invoice.ShootId);
        }

        var shoot = invoice.Shoot;
        var location = shoot.Location is null ? null : shoot.Location.City is null ? shoot.Location.Name : $"{shoot.Location.Name}, {shoot.Location.City}";
        return (invoice, new InvoiceShootInfo(shoot.Title, shoot.Start, shoot.End, location));
    }

    /// <summary>What has to be fixed before the invoice can be issued, in the current language; empty when it's ready.</summary>
    public async Task<IReadOnlyList<string>> CheckAsync(int invoiceId)
    {
        var (invoice, _) = await LoadForDocumentAsync(invoiceId);
        await using var db = await dbFactory.CreateDbContextAsync();
        var settings = await db.BusinessSettings.AsNoTracking().SingleAsync();

        var problems = new List<string>();
        if (!settings.IsComplete)
        {
            problems.Add(Texts.Get("Your business details are incomplete (Settings)."));
        }
        if (!invoice.Client.HasPostalAddress)
        {
            problems.Add(Texts.Format("The billing address of {0} is missing.", invoice.Client.Name));
        }
        if (invoice.Lines.Count == 0 || invoice.Lines.Any(l => string.IsNullOrWhiteSpace(l.Description)))
        {
            problems.Add(Texts.Get("Every invoice line needs a description."));
        }
        if (invoice.Totals.Total <= Money.Zero)
        {
            problems.Add(Texts.Get("The invoice total must be above zero."));
        }
        if (invoice.DueDate < invoice.IssueDate)
        {
            problems.Add(Texts.Get("The due date is before the invoice date."));
        }
        if (settings.IsComplete)
        {
            problems.AddRange(InvoiceQrBill.Problems(invoice));
        }

        return problems;
    }

    /// <summary>
    /// Numbers the invoice, freezes business and client data, records payments received so far
    /// and archives the PDF in the shoot's files. Returns the problems instead if it isn't ready.
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

        var invoice = await db.Invoices.Include(i => i.Lines).Include(i => i.Shoot).ThenInclude(s => s.Client)
            .SingleAsync(i => i.Id == invoiceId);
        EnsureDraft(invoice);

        InvoiceSnapshot.Apply(invoice, await db.BusinessSettings.SingleAsync(), invoice.Shoot.Client);
        invoice.AlreadyPaid = await ReceivedAsync(db, invoice.ShootId);
        invoice.Number = await NextNumberAsync(db, invoice.IssueDate.Year);
        invoice.Status = InvoiceStatus.Issued;
        invoice.IssuedAt = time.GetLocalNow().DateTime;

        var shoot = invoice.Shoot;
        var location = await db.Locations.Where(l => l.Id == shoot.LocationId)
            .Select(l => l.City == null ? l.Name : l.Name + ", " + l.City).SingleOrDefaultAsync();
        var pdf = new InvoiceDocument(invoice, new InvoiceShootInfo(shoot.Title, shoot.Start, shoot.End, location)).GeneratePdf();
        var culture = invoice.Language.Culture();
        var fileName = string.Format(culture, Texts.Get("Invoice {0}", culture), invoice.Number) + ".pdf";
        var attachment = await store.SaveAsync(new MemoryStream(pdf), fileName, invoice.ShootId);
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

    private static async Task<Money> ReceivedAsync(ApertureDbContext db, int shootId) =>
        (await db.Payments.Where(p => p.ShootId == shootId).Select(p => p.Amount).ToListAsync()).Sum();

    private static void EnsureDraft(Invoice invoice)
    {
        if (invoice.Status != InvoiceStatus.Draft)
        {
            throw new InvalidOperationException($"Invoice {invoice.Number} is issued and can no longer be changed.");
        }
    }
}
