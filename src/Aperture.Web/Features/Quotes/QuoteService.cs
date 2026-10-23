using Aperture.Web.Data;
using Aperture.Web.Data.Entities;
using Aperture.Web.Domain;
using Aperture.Web.Features.Attachments;
using Aperture.Web.Features.Invoices;
using Aperture.Web.Localization;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;

namespace Aperture.Web.Features.Quotes;

/// <summary>
/// Quotes for a shoot: drafted, sent (numbered, frozen, PDF archived), then accepted or declined by the client.
/// Accepting books the shoot at the quoted price; its invoice then starts from the quote's lines (see InvoiceService).
/// </summary>
public sealed class QuoteService(IDbContextFactory<ApertureDbContext> dbFactory, AttachmentStore store, TimeProvider time)
{
    /// <summary>How long a new quote holds.</summary>
    public const int ValidityDays = 30;

    private DateOnly Today => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    /// <summary>
    /// A draft in <paramref name="language"/>. A first quote starts from the shoot: its package (or "Photography") at the
    /// shoot's price, and what the package includes. A later one starts as a copy of the latest, to revise it.
    /// </summary>
    public async Task<int> CreateDraftAsync(int shootId, Language language)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await db.Quotes.AnyAsync(q => q.ShootId == shootId && q.Status == QuoteStatus.Draft))
        {
            throw new InvalidOperationException("This shoot already has a draft quote.");
        }

        var shoot = await db.Shoots.Include(s => s.Package).SingleAsync(s => s.Id == shootId);
        var previous = await db.Quotes.Include(q => q.Lines)
            .Where(q => q.ShootId == shootId)
            .OrderByDescending(q => q.Id)
            .FirstOrDefaultAsync();

        var quote = new Quote
        {
            ShootId = shootId,
            QuoteDate = Today,
            ValidUntil = Today.AddDays(ValidityDays),
            Language = language,
            Included = previous is null ? shoot.Package?.Included : previous.Included,
        };
        quote.Lines = previous is null
            ?
            [
                new QuoteLine
                {
                    Description = shoot.Package is { } package
                        ? $"{package.Name}: {shoot.Title}"
                        : InvoiceService.ShootLine(shoot.Title, language),
                    UnitPrice = shoot.QuotedPrice ?? Money.Zero,
                },
            ]
            : previous.Lines.OrderBy(l => l.SortOrder)
                .Select(l => new QuoteLine { Description = l.Description, Quantity = l.Quantity, UnitPrice = l.UnitPrice })
                .ToList();
        for (var i = 0; i < quote.Lines.Count; i++) quote.Lines[i].SortOrder = i;

        db.Quotes.Add(quote);
        await db.SaveChangesAsync();
        return quote.Id;
    }

    public async Task SaveDraftAsync(
        int quoteId, DateOnly quoteDate, DateOnly validUntil, Language language, string? included, IReadOnlyList<DraftLine> lines)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var quote = await db.Quotes.Include(q => q.Lines).SingleAsync(q => q.Id == quoteId);
        EnsureDraft(quote);

        quote.QuoteDate = quoteDate;
        quote.ValidUntil = validUntil;
        quote.Language = language;
        quote.Included = string.IsNullOrWhiteSpace(included) ? null : included.Trim();
        db.QuoteLines.RemoveRange(quote.Lines);
        quote.Lines = lines.Select((line, index) => new QuoteLine
        {
            SortOrder = index,
            Description = line.Description.Trim(),
            Quantity = line.Quantity,
            UnitPrice = line.UnitPrice,
        }).ToList();

        await db.SaveChangesAsync();
    }

    /// <summary>The quote as it will look: sent quotes as stored, drafts with current business and client data (not saved).</summary>
    public async Task<(Quote Quote, InvoiceShootInfo Shoot)> LoadForDocumentAsync(int quoteId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var quote = await db.Quotes.AsNoTracking()
            .Include(q => q.Lines)
            .Include(q => q.Shoot).ThenInclude(s => s.Client)
            .Include(q => q.Shoot).ThenInclude(s => s.Location)
            .SingleAsync(q => q.Id == quoteId);

        if (quote.Status == QuoteStatus.Draft)
        {
            InvoiceSnapshot.Apply(quote, await db.BusinessSettings.AsNoTracking().SingleAsync(), quote.Shoot.Client);
        }

        return (quote, InvoiceShootInfo.From(quote.Shoot));
    }

    /// <summary>What has to be fixed before the quote can be sent, in the current language; empty when it's ready.</summary>
    public async Task<IReadOnlyList<string>> CheckAsync(int quoteId)
    {
        var (quote, _) = await LoadForDocumentAsync(quoteId);
        await using var db = await dbFactory.CreateDbContextAsync();
        var settings = await db.BusinessSettings.AsNoTracking().SingleAsync();

        var problems = new List<string>();
        if (!settings.IsComplete)
        {
            problems.Add(Texts.Get("Your business details are incomplete (Settings)."));
        }
        if (quote.Lines.Count == 0 || quote.Lines.Any(l => string.IsNullOrWhiteSpace(l.Description)))
        {
            problems.Add(Texts.Get("Every line needs a description."));
        }
        if (quote.Totals.Total <= Money.Zero)
        {
            problems.Add(Texts.Get("The quote total must be above zero."));
        }
        if (quote.ValidUntil < quote.QuoteDate)
        {
            problems.Add(Texts.Get("The quote is valid until a date before the quote date."));
        }

        return problems;
    }

    /// <summary>
    /// Numbers the quote, freezes business and client data and archives the PDF in the shoot's files, to send to the
    /// client. Returns the problems instead if it isn't ready.
    /// </summary>
    public async Task<IReadOnlyList<string>> SendAsync(int quoteId)
    {
        var problems = await CheckAsync(quoteId);
        if (problems.Count > 0)
        {
            return problems;
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var quote = await db.Quotes.Include(q => q.Lines)
            .Include(q => q.Shoot).ThenInclude(s => s.Client)
            .Include(q => q.Shoot).ThenInclude(s => s.Location)
            .SingleAsync(q => q.Id == quoteId);
        EnsureDraft(quote);

        InvoiceSnapshot.Apply(quote, await db.BusinessSettings.SingleAsync(), quote.Shoot.Client);
        quote.Number = await NextNumberAsync(db, quote.QuoteDate.Year);
        quote.Status = QuoteStatus.Sent;
        quote.SentAt = time.GetLocalNow().DateTime;

        var pdf = new QuoteDocument(quote, InvoiceShootInfo.From(quote.Shoot)).GeneratePdf();
        var culture = quote.Language.Culture();
        var fileName = string.Format(culture, Texts.Get("Quote {0}", culture), quote.Number) + ".pdf";
        var attachment = await store.SaveAsync(new MemoryStream(pdf), fileName, quote.ShootId);
        quote.Pdf = attachment;

        await store.CommitAsync(async () =>
        {
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }, added: attachment, removed: null);

        return [];
    }

    /// <summary>The client said yes: an inquiry becomes booked, and the shoot's price is what the quote says (before VAT).</summary>
    public async Task AcceptAsync(int quoteId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var quote = await db.Quotes.Include(q => q.Lines).Include(q => q.Shoot).SingleAsync(q => q.Id == quoteId);
        EnsureSent(quote);

        quote.Status = QuoteStatus.Accepted;
        quote.AnsweredAt = time.GetLocalNow().DateTime;
        quote.Shoot.QuotedPrice = quote.Totals.Subtotal;
        if (quote.Shoot.Status == ShootStatus.Inquiry)
        {
            quote.Shoot.Status = ShootStatus.Booked;
        }

        await db.SaveChangesAsync();
    }

    /// <summary>The client said no: an inquiry is cancelled (a booked shoot stays as it is, e.g. after a quote for extras).</summary>
    public async Task DeclineAsync(int quoteId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var quote = await db.Quotes.Include(q => q.Shoot).SingleAsync(q => q.Id == quoteId);
        EnsureSent(quote);

        quote.Status = QuoteStatus.Declined;
        quote.AnsweredAt = time.GetLocalNow().DateTime;
        if (quote.Shoot.Status == ShootStatus.Inquiry)
        {
            quote.Shoot.Status = ShootStatus.Cancelled;
        }

        await db.SaveChangesAsync();
    }

    public async Task DeleteDraftAsync(int quoteId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var quote = await db.Quotes.SingleAsync(q => q.Id == quoteId);
        EnsureDraft(quote);
        db.Quotes.Remove(quote);
        await db.SaveChangesAsync();
    }

    /// <summary>"Q-2026-001", "Q-2026-002", … per year of the quote date.</summary>
    public static async Task<string> NextNumberAsync(ApertureDbContext db, int year)
    {
        var prefix = $"Q-{year}-";
        var numbers = await db.Quotes
            .Where(q => q.Number != null && q.Number.StartsWith(prefix))
            .Select(q => q.Number!)
            .ToListAsync();
        var last = numbers.Select(n => int.TryParse(n[prefix.Length..], out var sequence) ? sequence : 0).DefaultIfEmpty(0).Max();
        return $"{prefix}{last + 1:000}";
    }

    private static void EnsureDraft(Quote quote)
    {
        if (quote.Status != QuoteStatus.Draft)
        {
            throw new InvalidOperationException($"Quote {quote.Number} is sent and can no longer be changed.");
        }
    }

    private static void EnsureSent(Quote quote)
    {
        if (quote.Status != QuoteStatus.Sent)
        {
            throw new InvalidOperationException("Only sent quotes can be accepted or declined.");
        }
    }
}
