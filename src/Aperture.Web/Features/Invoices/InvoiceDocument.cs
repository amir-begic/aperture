using Codecrete.SwissQRBill.Generator;
using Aperture.Web.Data.Entities;
using Aperture.Web.Domain;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace Aperture.Web.Features.Invoices;

/// <summary>What a quote or an invoice says about the shoot it's for.</summary>
public sealed record InvoiceShootInfo(string Title, DateTime Start, DateTime End, string? Location)
{
    /// <summary>From a shoot loaded with its location: "Schloss Laufen, Laufen-Uhwiesen".</summary>
    public static InvoiceShootInfo From(Shoot shoot) => new(shoot.Title, shoot.Start, shoot.End,
        shoot.Location is null ? null : shoot.Location.City is null ? shoot.Location.Name : $"{shoot.Location.Name}, {shoot.Location.City}");
}

/// <summary>
/// The invoice PDF: sender, recipient in the window position of a Swiss envelope (right),
/// lines, totals, payment note and the QR-bill along the bottom edge of the last page. In the invoice's language.
/// </summary>
public sealed class InvoiceDocument(Invoice invoice, InvoiceShootInfo shoot) : IDocument
{
    private readonly DocumentLayout _layout = new(invoice);

    private bool IsDraft => invoice.Status == InvoiceStatus.Draft;

    private string Title => IsDraft ? _layout.T("Invoice (draft)") : _layout.TF("Invoice {0}", invoice.Number);

    public DocumentMetadata GetMetadata() => _layout.Metadata(Title);

    public void Compose(IDocumentContainer container)
    {
        var totals = invoice.Totals;
        var qrBillSvg = totals.AmountDue > Money.Zero ? InvoiceQrBill.GenerateSvg(invoice) : null;

        container.Page(page =>
        {
            _layout.Page(page, IsDraft);

            page.Content().Column(column =>
            {
                column.Item()
                    .PaddingHorizontal(DocumentLayout.PagePadding, Unit.Millimetre)
                    .PaddingTop(15, Unit.Millimetre)
                    .PaddingBottom(10, Unit.Millimetre)
                    .Column(body =>
                    {
                        body.Spacing(6, Unit.Millimetre);
                        body.Item().Element(_layout.Addresses);
                        body.Item().Element(c => _layout.Heading(c, Title,
                        [
                            (_layout.T("Invoice date"), invoice.IssueDate.ToString("dd.MM.yyyy")),
                            (_layout.T("Due date"), invoice.DueDate.ToString("dd.MM.yyyy")),
                        ], shoot));
                        body.Item().Element(c => _layout.Lines(c, invoice.Lines.OrderBy(l => l.SortOrder)
                            .Select(l => new DocumentLine(l.Description, l.Quantity, l.UnitPrice, l.Total))));
                        body.Item().Element(c => _layout.Totals(c, totals));
                        body.Item().Element(c => ComposePaymentNote(c, totals));
                    });

                // The QR-bill (210 × 105 mm) must sit at the bottom edge of a page; if it doesn't fit under
                // the content, ShowEntire moves it to a new page. Sized by width: QuestPDF's A4 is 595.0 pt
                // wide, a hair narrower than 210 mm, so a fixed 105 mm height would not fit the 2:1 ratio.
                if (qrBillSvg is not null)
                {
                    column.Item().ShowEntire().ExtendVertical().AlignBottom().Svg(qrBillSvg).FitWidth();
                }
            });
        });
    }

    private void ComposePaymentNote(IContainer container, InvoiceTotals totals) => container.Column(column =>
    {
        column.Spacing(2, Unit.Millimetre);
        if (totals.AmountDue == Money.Zero)
        {
            column.Item().Text(_layout.T("Paid in full. Thank you!")).Bold();
        }
        else
        {
            column.Item().Text(text =>
            {
                text.Span(_layout.TF("Please pay {0} by {1:dd.MM.yyyy} using the QR-bill below or by bank transfer to", totals.AmountDue, invoice.DueDate) + " ");
                text.Span(invoice.SellerIban is null ? "" : Payments.FormatIban(invoice.SellerIban)).Bold();
                text.Span($" ({invoice.Seller.Name}).");
            });
            column.Item().Text(_layout.T("Thank you for the booking!"));
        }

        _layout.VatNumber(column);
    });
}
