namespace Funk.Web.Domain;

public enum PaymentState
{
    /// <summary>Nothing to bill yet (no quote, no billed costs), so "paid" has no meaning.</summary>
    NoQuote,
    Unpaid,
    PartiallyPaid,
    Paid,
}

/// <summary>
/// The money side of one gig: what was agreed, spent and received.
/// <paramref name="BilledCosts"/> are costs charged to the client as their own line on top of the quote,
/// at cost or at a marked-up price.
/// </summary>
public sealed record GigFinancials(Money? Quote, Money Costs, Money Received, Money BilledCosts = default)
{
    /// <summary>Quote plus billed costs; null when there's nothing to bill yet.</summary>
    public Money? Total => Quote is null && BilledCosts == Money.Zero
        ? null
        : (Quote ?? Money.Zero) + BilledCosts;

    /// <summary>What's left after all costs; null when there's nothing to bill yet.</summary>
    public Money? Margin => Total - Costs;

    /// <summary>Still to be paid; never negative, null when there's nothing to bill yet.</summary>
    public Money? Outstanding => Total is { } total
        ? (Received >= total ? Money.Zero : total - Received)
        : null;

    public PaymentState PaymentState => Total switch
    {
        null => PaymentState.NoQuote,
        var total when Received >= total.Value => PaymentState.Paid,
        _ when Received > Money.Zero => PaymentState.PartiallyPaid,
        _ => PaymentState.Unpaid,
    };
}
