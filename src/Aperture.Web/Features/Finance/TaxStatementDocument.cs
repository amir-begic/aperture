using Aperture.Web.Data.Entities;
using Aperture.Web.Domain;
using Aperture.Web.Features.Invoices;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Aperture.Web.Features.Finance;

/// <summary>
/// The yearly income statement for the tax return: summary first, then every income and cost line with the
/// number of its receipt, and the equipment register with depreciation.
/// </summary>
public sealed class TaxStatementDocument(TaxExportData data) : IDocument
{
    static TaxStatementDocument() => InvoiceFonts.EnsureRegistered();

    private IncomeStatement Statement => data.Statement;

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Income statement {Statement.Year}",
        Author = data.Settings.BusinessName is { Length: > 0 } business ? business : data.Settings.OwnerName,
    };

    public void Compose(IDocumentContainer container) => container.Page(page =>
    {
        page.Size(PageSizes.A4);
        page.Margin(18, Unit.Millimetre);
        page.DefaultTextStyle(text => text.FontSize(9).FontFamily(InvoiceFonts.Family).FontColor(Colors.Grey.Darken4));

        page.Header().PaddingBottom(6, Unit.Millimetre).Element(ComposeHeader);
        page.Content().Column(column =>
        {
            column.Spacing(8, Unit.Millimetre);
            column.Item().Element(ComposeSummary);
            column.Item().Element(ComposeIncome);
            column.Item().Element(ComposeCosts);
            column.Item().Element(ComposeEquipment);
        });
        page.Footer().Row(row =>
        {
            row.RelativeItem().Text($"Income statement {Statement.Year} · created {data.CreatedOn:dd.MM.yyyy}").FontSize(7).FontColor(Colors.Grey.Darken1);
            row.RelativeItem().AlignRight().Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(7).FontColor(Colors.Grey.Darken1));
                text.Span("Page ");
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
            });
        });
    });

    private void ComposeHeader(IContainer container) => container.Row(row =>
    {
        var settings = data.Settings;
        row.RelativeItem().Column(column =>
        {
            column.Item().Text($"Income statement {Statement.Year}").FontSize(16).Bold();
            column.Item().Text($"1.1.–31.12.{Statement.Year} · cash basis · amounts in CHF").FontColor(Colors.Grey.Darken1);
        });
        row.RelativeItem().AlignRight().Column(column =>
        {
            if (!string.IsNullOrWhiteSpace(settings.BusinessName)) column.Item().AlignRight().Text(settings.BusinessName).Bold();
            column.Item().AlignRight().Text(settings.OwnerName);
            column.Item().AlignRight().Text(settings.Street);
            column.Item().AlignRight().Text($"{settings.PostalCode} {settings.City}");
            column.Item().AlignRight().Text(settings.IsVatRegistered ? $"VAT no. {settings.VatNumber}" : "Not registered for VAT");
        });
    });

    private void ComposeSummary(IContainer container) => container.Column(column =>
    {
        column.Item().PaddingBottom(2, Unit.Millimetre).Text("Summary").FontSize(12).Bold();
        column.Item().Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.ConstantColumn(32, Unit.Millimetre);
            });

            void Line(string label, Money amount, bool bold = false, bool indent = false, bool topBorder = false)
            {
                var labelCell = table.Cell().PaddingVertical(2).PaddingLeft(indent ? 4 : 0, Unit.Millimetre);
                var amountCell = table.Cell().PaddingVertical(2);
                if (topBorder)
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

            Line("Income from DJ services", Statement.TotalIncome, bold: true);
            foreach (var (category, amount) in Statement.CostsByCategory)
            {
                Line(category.Text(), -amount, indent: true);
            }
            if (Statement.TotalDepreciation != Money.Zero) Line("Depreciation of equipment", -Statement.TotalDepreciation, indent: true);
            if (Statement.DisposalResult != Money.Zero) Line("Result from sale of equipment", Statement.DisposalResult, indent: true);
            Line(Statement.Result >= Money.Zero ? "Profit" : "Loss", Statement.Result, bold: true, topBorder: true);
        });
    });

    /// <summary>A table column: fixed width in mm, or a relative share of the rest; amounts align right.</summary>
    private sealed record Column(string Title, float? WidthMm = null, float Share = 1, bool Right = false);

    private void ComposeIncome(IContainer container) => Section(container, "Income", "Payments received in the year.",
        [new("Date", 18), new("Gig", Share: 3), new("Client", Share: 2), new("Invoice", 18), new("Doc.", 12), new("Amount", 24, Right: true)],
        Statement.Income.Select(income => new[]
        {
            income.Date.ToString("dd.MM.yyyy"),
            income.Note is null ? income.GigTitle : $"{income.GigTitle} ({income.Note})",
            income.ClientName,
            income.InvoiceNumber ?? "",
            data.Documents.NumberOf(income.Proof) ?? "",
            income.Amount.ToAmountString(),
        }),
        total: Statement.TotalIncome);

    private void ComposeCosts(IContainer container) => Section(container, "Costs", "Costs paid in the year, including costs of gigs.",
        [new("Date", 18), new("Category", 30), new("Description", Share: 3), new("Gig", Share: 2), new("Doc.", 12), new("Amount", 24, Right: true)],
        Statement.Expenses.Select(cost => new[]
        {
            cost.Date.ToString("dd.MM.yyyy"),
            cost.Category.Text(),
            cost.Vendor is null ? cost.Description : $"{cost.Description}, {cost.Vendor}",
            cost.GigTitle ?? "",
            data.Documents.NumberOf(cost.Receipt) ?? "",
            cost.Amount.ToAmountString(),
        }),
        total: Statement.TotalCosts);

    private void ComposeEquipment(IContainer container) => Section(container, "Equipment and depreciation",
        Statement.ProRataFirstYear
            ? "Declining balance on the book value, pro rata by month in the year of purchase and sale."
            : "Declining balance on the book value; full rate in the year of purchase, pro rata by month in the year of sale.",
        [
            new("Equipment", Share: 3), new("Bought", 18), new("Price", 20, Right: true), new("Rate", 11, Right: true),
            new("Start", 20, Right: true), new("Depreciation", 22, Right: true), new("End", 24, Right: true), new("Doc.", 12),
        ],
        Statement.Assets.Select(entry =>
        {
            var (asset, year) = (entry.Asset, entry.Year);
            var end = year.DisposalResult is { } result
                ? $"sold, {(result >= Money.Zero ? "+" : "")}{result.ToAmountString()}"
                : year.Closing.ToAmountString();
            return new[]
            {
                asset.Name,
                asset.PurchaseDate.ToString("dd.MM.yyyy"),
                asset.Cost.ToAmountString(),
                $"{asset.DepreciationRatePercent:0.#}%",
                year.Opening.ToAmountString(),
                year.Depreciation.ToAmountString(),
                end,
                data.Documents.NumberOf(asset.Receipt) ?? "",
            };
        }),
        total: Statement.TotalDepreciation, totalColumn: 5);

    private static void Section(
        IContainer container, string title, string description, Column[] columns, IEnumerable<string[]> rows,
        Money total, int? totalColumn = null) =>
        container.Column(column =>
        {
            column.Item().Text(title).FontSize(12).Bold();
            column.Item().PaddingBottom(2, Unit.Millimetre).Text(description).FontColor(Colors.Grey.Darken1);
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(definition =>
                {
                    foreach (var c in columns)
                    {
                        if (c.WidthMm is { } width) definition.ConstantColumn(width, Unit.Millimetre);
                        else definition.RelativeColumn(c.Share);
                    }
                });

                table.Header(header =>
                {
                    foreach (var c in columns)
                    {
                        var cell = header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Darken2).PaddingVertical(3).PaddingHorizontal(3);
                        (c.Right ? cell.AlignRight() : cell).Text(c.Title).Bold();
                    }
                });

                foreach (var row in rows)
                {
                    for (var i = 0; i < columns.Length; i++)
                    {
                        var cell = table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).PaddingHorizontal(3);
                        (columns[i].Right ? cell.AlignRight() : cell).Text(row[i]);
                    }
                }

                var position = totalColumn ?? columns.Length - 1;
                for (var i = 0; i < columns.Length; i++)
                {
                    var cell = table.Cell().PaddingVertical(3).PaddingHorizontal(3);
                    if (i == 0) cell.Text("Total").Bold();
                    else if (i == position) cell.AlignRight().Text(total.ToAmountString()).Bold();
                }
            });
        });
}
