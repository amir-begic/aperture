using Funk.Web.Domain;

namespace Funk.Tests.Domain;

public class GigFinancialsTests
{
    private static Money Chf(decimal value) => Money.FromChf(value);

    [Fact]
    public void Margin_and_outstanding()
    {
        var financials = new GigFinancials(Quote: Chf(1800), Costs: Chf(350), Received: Chf(500));

        Assert.Equal(Chf(1450), financials.Margin);
        Assert.Equal(Chf(1300), financials.Outstanding);
        Assert.Equal(PaymentState.PartiallyPaid, financials.PaymentState);
    }

    [Theory]
    [InlineData(0, PaymentState.Unpaid)]
    [InlineData(1799.95, PaymentState.PartiallyPaid)]
    [InlineData(1800, PaymentState.Paid)]
    [InlineData(1900, PaymentState.Paid)]   // tip or rounding: still paid, nothing outstanding
    public void Payment_state(decimal received, PaymentState expected)
    {
        var financials = new GigFinancials(Chf(1800), Money.Zero, Chf(received));

        Assert.Equal(expected, financials.PaymentState);
        Assert.True(financials.Outstanding >= Money.Zero);
    }

    [Fact]
    public void Without_a_quote_there_is_no_margin_or_outstanding_amount()
    {
        var financials = new GigFinancials(Quote: null, Costs: Chf(100), Received: Chf(200));

        Assert.Null(financials.Margin);
        Assert.Null(financials.Outstanding);
        Assert.Equal(PaymentState.NoQuote, financials.PaymentState);
    }

    [Fact]
    public void Costs_above_quote_give_a_negative_margin()
    {
        Assert.Equal(Chf(-50), new GigFinancials(Chf(300), Chf(350), Money.Zero).Margin);
    }

    [Fact]
    public void Costs_billed_on_top_of_the_quote_raise_the_total()
    {
        // Light rental costs 350 and is billed at 450 as its own line; the PA is absorbed in the quote.
        var financials = new GigFinancials(Quote: Chf(1800), Costs: Chf(350 + 200), Received: Chf(1800), BilledCosts: Chf(450));

        Assert.Equal(Chf(2250), financials.Total);
        Assert.Equal(Chf(1700), financials.Margin);
        Assert.Equal(Chf(450), financials.Outstanding);
        Assert.Equal(PaymentState.PartiallyPaid, financials.PaymentState);
    }

    [Fact]
    public void Passing_a_cost_on_at_cost_leaves_the_margin_unchanged()
    {
        var withoutRental = new GigFinancials(Chf(1800), Money.Zero, Money.Zero);
        var rentalAtCost = new GigFinancials(Chf(1800), Chf(350), Money.Zero, BilledCosts: Chf(350));

        Assert.Equal(withoutRental.Margin, rentalAtCost.Margin);
    }

    [Fact]
    public void Billed_costs_without_a_quote_are_still_something_to_bill()
    {
        var financials = new GigFinancials(Quote: null, Costs: Chf(350), Received: Money.Zero, BilledCosts: Chf(400));

        Assert.Equal(Chf(400), financials.Total);
        Assert.Equal(Chf(50), financials.Margin);
        Assert.Equal(PaymentState.Unpaid, financials.PaymentState);
    }
}
