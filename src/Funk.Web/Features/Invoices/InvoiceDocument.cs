using Codecrete.SwissQRBill.Generator;
using Funk.Web.Data.Entities;
using Funk.Web.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Funk.Web.Features.Invoices;

/// <summary>What the invoice says about the gig it bills.</summary>
public sealed record InvoiceGigInfo(string Title, DateTime Start, DateTime End, string? Venue);

/// <summary>
/// The invoice PDF: sender, recipient in the window position of a Swiss envelope (right),
/// lines, totals, payment note and the QR-bill along the bottom edge of the last page.
/// </summary>
public sealed class InvoiceDocument(Invoice invoice, InvoiceGigInfo gig) : IDocument
{
    private const float PagePadding = 20; // mm, everywhere except the QR-bill, which spans the full width

    static InvoiceDocument() => InvoiceFonts.EnsureRegistered();

    private bool IsDraft => invoice.Status == InvoiceStatus.Draft;

    public DocumentMetadata GetMetadata() => new()
    {
        Title = IsDraft ? "Invoice (draft)" : $"Invoice {invoice.Number}",
        Author = invoice.Seller.Company ?? invoice.Seller.Name,
    };

    public void Compose(IDocumentContainer container)
    {
        var totals = invoice.Totals;
        var qrBillSvg = totals.AmountDue > Money.Zero ? InvoiceQrBill.GenerateSvg(invoice) : null;

        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.DefaultTextStyle(text => text.FontSize(10).FontFamily(InvoiceFonts.Family).FontColor(Colors.Grey.Darken4));

            if (IsDraft)
            {
                page.Background().AlignCenter().AlignMiddle().Rotate(-30)
                    .Text("DRAFT").FontSize(120).Bold().FontColor(Colors.Grey.Lighten3);
            }

            page.Content().Column(column =>
            {
                column.Item()
                    .PaddingHorizontal(PagePadding, Unit.Millimetre)
                    .PaddingTop(15, Unit.Millimetre)
                    .PaddingBottom(10, Unit.Millimetre)
                    .Column(body =>
                    {
                        body.Spacing(6, Unit.Millimetre);
                        body.Item().Element(ComposeAddresses);
                        body.Item().Element(ComposeHeading);
                        body.Item().Element(ComposeLines);
                        body.Item().Element(c => ComposeTotals(c, totals));
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

    private void ComposeAddresses(IContainer container) => container.Row(row =>
    {
        row.RelativeItem().Column(seller =>
        {
            seller.Item().Text(invoice.Seller.Company ?? invoice.Seller.Name).Bold().FontSize(12);
            if (invoice.Seller.Company is not null)
            {
                seller.Item().Text(invoice.Seller.Name);
            }
            seller.Item().Text(invoice.Seller.Street);
            seller.Item().Text($"{invoice.Seller.PostalCode} {invoice.Seller.City}");
            if (invoice.SellerEmail is not null) seller.Item().PaddingTop(4).Text(invoice.SellerEmail);
            if (invoice.SellerPhone is not null) seller.Item().Text(invoice.SellerPhone);
        });

        // Recipient where a right-hand window envelope shows it (Swiss standard).
        row.ConstantItem(85, Unit.Millimetre).PaddingTop(30, Unit.Millimetre).Column(client =>
        {
            if (invoice.Client.Company is not null) client.Item().Text(invoice.Client.Company);
            client.Item().Text(invoice.Client.Name);
            if (invoice.Client.Street is not null) client.Item().Text(invoice.Client.Street);
            if (invoice.Client.PostalCode is not null || invoice.Client.City is not null)
            {
                client.Item().Text($"{invoice.Client.PostalCode} {invoice.Client.City}".Trim());
            }
            if (invoice.Client.CountryCode != "CH") client.Item().Text(invoice.Client.CountryCode);
        });
    });

    private void ComposeHeading(IContainer container) => container.Column(column =>
    {
        column.Spacing(3, Unit.Millimetre);
        column.Item().Text(IsDraft ? "Invoice (draft)" : $"Invoice {invoice.Number}").FontSize(18).Bold();
        column.Item().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(30, Unit.Millimetre);
                columns.RelativeColumn();
            });

            void Fact(string label, string value)
            {
                table.Cell().Text(label).FontColor(Colors.Grey.Darken1);
                table.Cell().Text(value);
            }

            Fact("Invoice date", invoice.IssueDate.ToString("dd.MM.yyyy"));
            Fact("Due date", invoice.DueDate.ToString("dd.MM.yyyy"));
            Fact("Event", gig.Title);
            Fact("Event date", $"{gig.Start:ddd dd.MM.yyyy}, {GigSchedule.FormatTimes(gig.Start, gig.End)}");
            if (gig.Venue is not null) Fact("Venue", gig.Venue);
        });
    });

    private void ComposeLines(IContainer container) => container.Table(table =>
    {
        table.ColumnsDefinition(columns =>
        {
            columns.RelativeColumn();
            columns.ConstantColumn(16, Unit.Millimetre);
            columns.ConstantColumn(28, Unit.Millimetre);
            columns.ConstantColumn(28, Unit.Millimetre);
        });

        table.Header(header =>
        {
            static IContainer HeaderCell(IContainer cell) =>
                cell.BorderBottom(1).BorderColor(Colors.Grey.Darken2).PaddingVertical(4);

            header.Cell().Element(HeaderCell).Text("Description").Bold();
            header.Cell().Element(HeaderCell).AlignRight().Text("Qty").Bold();
            header.Cell().Element(HeaderCell).AlignRight().Text("Price CHF").Bold();
            header.Cell().Element(HeaderCell).AlignRight().Text("Amount CHF").Bold();
        });

        foreach (var line in invoice.Lines.OrderBy(l => l.SortOrder))
        {
            static IContainer Cell(IContainer cell) =>
                cell.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4);

            table.Cell().Element(Cell).Text(line.Description);
            table.Cell().Element(Cell).AlignRight().Text(line.Quantity.ToString("0.##"));
            table.Cell().Element(Cell).AlignRight().Text(line.UnitPrice.ToAmountString());
            table.Cell().Element(Cell).AlignRight().Text(line.Total.ToAmountString());
        }
    });

    private static void ComposeTotals(IContainer container, InvoiceTotals totals) =>
        container.AlignRight().Width(85, Unit.Millimetre).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.ConstantColumn(28, Unit.Millimetre);
            });

            void Row(string label, Money amount, bool bold = false, bool line = false)
            {
                var labelCell = table.Cell().PaddingVertical(2);
                var amountCell = table.Cell().PaddingVertical(2);
                if (line)
                {
                    labelCell = labelCell.BorderTop(1).BorderColor(Colors.Grey.Darken2);
                    amountCell = amountCell.BorderTop(1).BorderColor(Colors.Grey.Darken2);
                }

                var labelText = labelCell.Text(label);
                var amountText = amountCell.AlignRight().Text(amount.ToAmountString());
                if (bold)
                {
                    labelText.Bold();
                    amountText.Bold();
                }
            }

            var hasAdjustments = totals.VatRatePercent is not null || totals.Rounding != Money.Zero;
            if (hasAdjustments) Row("Subtotal", totals.Subtotal);
            if (totals.VatRatePercent is { } rate) Row($"VAT {rate:0.0#} %", totals.Vat);
            if (totals.Rounding != Money.Zero) Row("Rounding", totals.Rounding);
            Row("Total CHF", totals.Total, bold: true, line: hasAdjustments);

            if (totals.AlreadyPaid > Money.Zero)
            {
                Row("Already paid", -totals.AlreadyPaid);
                Row("Amount due CHF", totals.AmountDue, bold: true, line: true);
            }
        });

    private void ComposePaymentNote(IContainer container, InvoiceTotals totals) => container.Column(column =>
    {
        column.Spacing(2, Unit.Millimetre);
        if (totals.AmountDue == Money.Zero)
        {
            column.Item().Text("Paid in full. Thank you!").Bold();
        }
        else
        {
            column.Item().Text(text =>
            {
                text.Span($"Please pay {totals.AmountDue} by {invoice.DueDate:dd.MM.yyyy} ");
                text.Span("using the QR-bill below or by bank transfer to ");
                text.Span(invoice.SellerIban is null ? "" : Payments.FormatIban(invoice.SellerIban)).Bold();
                text.Span($" ({invoice.Seller.Name}).");
            });
            column.Item().Text("Thank you for the booking!");
        }

        if (invoice.SellerVatNumber is not null)
        {
            column.Item().PaddingTop(2, Unit.Millimetre).Text($"VAT no. {invoice.SellerVatNumber}").FontSize(8).FontColor(Colors.Grey.Darken1);
        }
    });
}
