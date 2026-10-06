namespace Funk.Web.Domain;

/// <summary>One invoice line as far as the calculation is concerned.</summary>
public readonly record struct InvoiceLineAmount(decimal Quantity, Money UnitPrice)
{
    /// <summary>Rounded to the Rappen.</summary>
    public Money Total => UnitPrice * Quantity;
}

/// <summary>
/// Subtotal → VAT (if registered) → rounding to 5 Rappen → total → minus what was already paid.
/// The QR-bill asks for <see cref="AmountDue"/>.
/// </summary>
public sealed record InvoiceTotals(
    Money Subtotal,
    decimal? VatRatePercent,
    Money Vat,
    Money Rounding,
    Money Total,
    Money AlreadyPaid,
    Money AmountDue)
{
    public static InvoiceTotals Calculate(IEnumerable<InvoiceLineAmount> lines, decimal? vatRatePercent, Money alreadyPaid)
    {
        var subtotal = lines.Sum(line => line.Total);
        var vat = vatRatePercent is { } rate ? subtotal * (rate / 100m) : Money.Zero;
        var exact = subtotal + vat;
        var total = exact.RoundToFiveRappen();
        var amountDue = total - alreadyPaid;

        return new InvoiceTotals(
            subtotal,
            vatRatePercent,
            vat,
            Rounding: total - exact,
            total,
            alreadyPaid,
            AmountDue: amountDue > Money.Zero ? amountDue : Money.Zero);
    }
}
