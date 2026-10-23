using Aperture.Web.Domain;

namespace Aperture.Web.Features.Invoices;

/// <summary>A line of a draft quote or invoice as the form edits it: the price in CHF as typed.</summary>
public sealed class DraftLineEdit
{
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    public decimal PriceChf { get; set; }

    public Money Total => new InvoiceLineAmount(Quantity, Money.FromChf(PriceChf)).Total;

    public static DraftLineEdit From(string description, decimal quantity, Money unitPrice) =>
        new() { Description = description, Quantity = quantity, PriceChf = unitPrice.Chf };

    public DraftLine ToDraftLine() => new(Description, Quantity, Money.FromChf(PriceChf));

    /// <summary>The draft's totals as they stand in the form.</summary>
    public static InvoiceTotals Totals(IEnumerable<DraftLineEdit> lines, decimal? vatRatePercent, Money alreadyPaid) =>
        InvoiceTotals.Calculate(lines.Select(l => new InvoiceLineAmount(l.Quantity, Money.FromChf(l.PriceChf))), vatRatePercent, alreadyPaid);
}
