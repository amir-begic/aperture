using Aperture.Web.Data.Entities;
using Aperture.Web.Features.Invoices;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Aperture.Web.Features.Quotes;

/// <summary>
/// The quote PDF: laid out like the invoice (see DocumentLayout), with what's included and how long the offer holds
/// instead of payment details. In the quote's language.
/// </summary>
public sealed class QuoteDocument(Quote quote, InvoiceShootInfo shoot) : IDocument
{
    private readonly DocumentLayout _layout = new(quote);

    private bool IsDraft => quote.Status == QuoteStatus.Draft;

    private string Title => IsDraft ? _layout.T("Quote (draft)") : _layout.TF("Quote {0}", quote.Number);

    public DocumentMetadata GetMetadata() => _layout.Metadata(Title);

    public void Compose(IDocumentContainer container) => container.Page(page =>
    {
        _layout.Page(page, IsDraft);

        page.Content()
            .PaddingHorizontal(DocumentLayout.PagePadding, Unit.Millimetre)
            .PaddingVertical(15, Unit.Millimetre)
            .Column(body =>
            {
                body.Spacing(6, Unit.Millimetre);
                body.Item().Element(_layout.Addresses);
                body.Item().Element(c => _layout.Heading(c, Title,
                [
                    (_layout.T("Quote date"), quote.QuoteDate.ToString("dd.MM.yyyy")),
                    (_layout.T("Valid until"), quote.ValidUntil.ToString("dd.MM.yyyy")),
                ], shoot));
                body.Item().Element(c => _layout.Lines(c, quote.Lines.OrderBy(l => l.SortOrder)
                    .Select(l => new DocumentLine(l.Description, l.Quantity, l.UnitPrice, l.Total))));
                body.Item().Element(c => _layout.Totals(c, quote.Totals));
                body.Item().Element(ComposeClosing);
            });
    });

    private void ComposeClosing(IContainer container) => container.Column(column =>
    {
        column.Spacing(2, Unit.Millimetre);
        if (!string.IsNullOrWhiteSpace(quote.Included))
        {
            column.Item().Text(_layout.T("Included")).Bold();
            column.Item().PaddingBottom(2, Unit.Millimetre).Text(quote.Included);
        }

        column.Item().Text(_layout.TF("This quote is valid until {0:dd.MM.yyyy}. To book, simply confirm it by email or phone.", quote.ValidUntil));
        column.Item().Text(_layout.T("I look forward to hearing from you!"));
        _layout.VatNumber(column);
    });
}
