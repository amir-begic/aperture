using Aperture.Web.Domain;

namespace Aperture.Tests.Domain;

public class InvoiceTotalsTests
{
    private static Money Chf(decimal value) => Money.FromChf(value);

    [Fact]
    public void Without_vat_the_total_is_the_sum_of_the_lines()
    {
        var totals = InvoiceTotals.Calculate(
            [new(1, Chf(1800)), new(1, Chf(450))],
            vatRatePercent: null,
            alreadyPaid: Money.Zero);

        Assert.Equal(Chf(2250), totals.Subtotal);
        Assert.Equal(Money.Zero, totals.Vat);
        Assert.Equal(Money.Zero, totals.Rounding);
        Assert.Equal(Chf(2250), totals.Total);
        Assert.Equal(Chf(2250), totals.AmountDue);
    }

    [Fact]
    public void Quantities_multiply_and_round_per_line()
    {
        // 2.5 hours extension at 133.33 = 333.325 -> 333.33
        Assert.Equal(Chf(333.33m), new InvoiceLineAmount(2.5m, Chf(133.33m)).Total);
    }

    [Fact]
    public void Vat_is_added_and_the_total_rounded_to_five_rappen()
    {
        var totals = InvoiceTotals.Calculate([new(1, Chf(1234.55m))], vatRatePercent: 8.1m, alreadyPaid: Money.Zero);

        Assert.Equal(Chf(100.00m), totals.Vat);          // 99.99855
        Assert.Equal(Chf(1334.55m), totals.Total);       // already on a 5-Rappen step
        Assert.Equal(Money.Zero, totals.Rounding);

        var odd = InvoiceTotals.Calculate([new(1, Chf(1000))], vatRatePercent: 8.1m, alreadyPaid: Money.Zero);
        Assert.Equal(Chf(81.00m), odd.Vat);
        Assert.Equal(Chf(1081.00m), odd.Total);

        var rounded = InvoiceTotals.Calculate([new(1, Chf(99.99m))], vatRatePercent: null, alreadyPaid: Money.Zero);
        Assert.Equal(Chf(100.00m), rounded.Total);
        Assert.Equal(Chf(0.01m), rounded.Rounding);
    }

    [Fact]
    public void Deposits_already_paid_reduce_the_amount_due()
    {
        var totals = InvoiceTotals.Calculate([new(1, Chf(2250))], null, alreadyPaid: Chf(500));

        Assert.Equal(Chf(2250), totals.Total);
        Assert.Equal(Chf(1750), totals.AmountDue);
    }

    [Fact]
    public void Amount_due_never_goes_negative()
    {
        Assert.Equal(Money.Zero, InvoiceTotals.Calculate([new(1, Chf(100))], null, alreadyPaid: Chf(150)).AmountDue);
    }
}
