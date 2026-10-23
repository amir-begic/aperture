using System.Globalization;
using Aperture.Web.Data.Entities;
using Aperture.Web.Domain;
using Aperture.Web.Localization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Aperture.Web.Features.Invoices;

/// <summary>A line as printed: description, quantity, unit price and amount.</summary>
public sealed record DocumentLine(string Description, decimal Quantity, Money UnitPrice, Money Total);

/// <summary>
/// The parts quotes and invoices share, so they look alike: page, addresses, title with facts, lines and totals.
/// In the document's language.
/// </summary>
public sealed class DocumentLayout(ClientDocument document)
{
    public const float PagePadding = 20; // mm

    static DocumentLayout() => InvoiceFonts.EnsureRegistered();

    public CultureInfo Culture { get; } = document.Language.Culture();

    public string T(string key) => Texts.Get(key, Culture);

    public string TF(string key, params object?[] args) => string.Format(Culture, T(key), args);

    public DocumentMetadata Metadata(string title) => new()
    {
        Title = title,
        Author = document.Seller.Company ?? document.Seller.Name,
    };

    /// <summary>A4 without margins (the QR-bill spans the full width), the default text style, and DRAFT across drafts.</summary>
    public void Page(PageDescriptor page, bool isDraft)
    {
        page.Size(PageSizes.A4);
        page.Margin(0);
        page.DefaultTextStyle(text => text.FontSize(10).FontFamily(InvoiceFonts.Family).FontColor(Colors.Grey.Darken4));

        if (isDraft)
        {
            page.Background().AlignCenter().AlignMiddle().Rotate(-30)
                .Text(T("Draft").ToUpper(Culture)).FontSize(120).Bold().FontColor(Colors.Grey.Lighten3);
        }
    }

    public void Addresses(IContainer container) => container.Row(row =>
    {
        row.RelativeItem().Column(seller =>
        {
            seller.Item().Text(document.Seller.Company ?? document.Seller.Name).Bold().FontSize(12);
            if (document.Seller.Company is not null)
            {
                seller.Item().Text(document.Seller.Name);
            }
            seller.Item().Text(document.Seller.Street);
            seller.Item().Text($"{document.Seller.PostalCode} {document.Seller.City}");
            if (document.SellerEmail is not null) seller.Item().PaddingTop(4).Text(document.SellerEmail);
            if (document.SellerPhone is not null) seller.Item().Text(document.SellerPhone);
        });

        // Recipient where a right-hand window envelope shows it (Swiss standard).
        row.ConstantItem(85, Unit.Millimetre).PaddingTop(30, Unit.Millimetre).Column(client =>
        {
            if (document.Client.Company is not null) client.Item().Text(document.Client.Company);
            client.Item().Text(document.Client.Name);
            if (document.Client.Street is not null) client.Item().Text(document.Client.Street);
            if (document.Client.PostalCode is not null || document.Client.City is not null)
            {
                client.Item().Text($"{document.Client.PostalCode} {document.Client.City}".Trim());
            }
            if (document.Client.CountryCode != "CH") client.Item().Text(document.Client.CountryCode);
        });
    });

    /// <summary>The title, then label and value pairs: the document's dates, followed by the shoot's.</summary>
    public void Heading(IContainer container, string title, IEnumerable<(string Label, string Value)> facts, InvoiceShootInfo shoot) =>
        container.Column(column =>
        {
            column.Spacing(3, Unit.Millimetre);
            column.Item().Text(title).FontSize(18).Bold();
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

                foreach (var (label, value) in facts) Fact(label, value);
                Fact(T("Invoice.Event"), shoot.Title);
                Fact(T("Event date"), string.Format(Culture, "{0:ddd dd.MM.yyyy}, {1}", shoot.Start, ShootSchedule.FormatTimes(shoot.Start, shoot.End)));
                if (shoot.Location is not null) Fact(T("Location"), shoot.Location);
            });
        });

    public void Lines(IContainer container, IEnumerable<DocumentLine> lines) => container.Table(table =>
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

            header.Cell().Element(HeaderCell).Text(T("Description")).Bold();
            header.Cell().Element(HeaderCell).AlignRight().Text(T("Qty")).Bold();
            header.Cell().Element(HeaderCell).AlignRight().Text(T("Price CHF")).Bold();
            header.Cell().Element(HeaderCell).AlignRight().Text(T("Amount CHF")).Bold();
        });

        foreach (var line in lines)
        {
            static IContainer Cell(IContainer cell) =>
                cell.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4);

            table.Cell().Element(Cell).Text(line.Description);
            table.Cell().Element(Cell).AlignRight().Text(line.Quantity.ToString("0.##"));
            table.Cell().Element(Cell).AlignRight().Text(line.UnitPrice.ToAmountString());
            table.Cell().Element(Cell).AlignRight().Text(line.Total.ToAmountString());
        }
    });

    public void Totals(IContainer container, InvoiceTotals totals) =>
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
            if (hasAdjustments) Row(T("Subtotal"), totals.Subtotal);
            if (totals.VatRatePercent is { } rate) Row(TF("VAT {0:0.0#} %", rate), totals.Vat);
            if (totals.Rounding != Money.Zero) Row(T("Rounding"), totals.Rounding);
            Row(T("Total CHF"), totals.Total, bold: true, line: hasAdjustments);

            if (totals.AlreadyPaid > Money.Zero)
            {
                Row(T("Already paid"), -totals.AlreadyPaid);
                Row(T("Amount due CHF"), totals.AmountDue, bold: true, line: true);
            }
        });

    /// <summary>The VAT number in small print, for VAT-registered businesses.</summary>
    public void VatNumber(ColumnDescriptor column)
    {
        if (document.SellerVatNumber is not null)
        {
            column.Item().PaddingTop(2, Unit.Millimetre).Text(TF("VAT no. {0}", document.SellerVatNumber)).FontSize(8).FontColor(Colors.Grey.Darken1);
        }
    }
}
