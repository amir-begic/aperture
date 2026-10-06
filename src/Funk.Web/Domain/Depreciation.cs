namespace Funk.Web.Domain;

/// <summary>One year of an asset's depreciation schedule.</summary>
/// <param name="DisposalResult">Sale proceeds minus remaining book value in the year it was sold (gain, or loss if negative).</param>
public sealed record DepreciationYear(int Year, Money Opening, Money Depreciation, Money Closing, Money? DisposalResult);

/// <summary>
/// Declining-balance depreciation ("degressiv, vom Buchwert") as in the ESTV's Merkblatt A 1995: each year the rate
/// applies to the remaining book value. The ESTV rates are maximums; a lower rate is always allowed.
/// <para>
/// Year of purchase: the full annual rate by default, as St. Gallen practice allows ("Die Abschreibungen werden nicht
/// pro rata temporis gekürzt", StB 41 Nr. 1); or, with <c>proRataFirstYear</c>, by month (the month of purchase counts).
/// Year of sale: by month up to the sale (the month of sale doesn't count).
/// </para>
/// </summary>
public static class Depreciation
{
    /// <summary>Remaining book values below this are written off completely instead of shrinking forever.</summary>
    private static readonly Money WriteOffThreshold = Money.FromChf(1);

    public static IReadOnlyList<DepreciationYear> Schedule(
        DateOnly purchased, Money cost, decimal ratePercent, DateOnly? disposedOn, Money? saleProceeds, int untilYear,
        bool proRataFirstYear = false)
    {
        var years = new List<DepreciationYear>();
        var bookValue = cost;
        var lastYear = Math.Min(untilYear, disposedOn?.Year ?? untilYear);

        for (var year = purchased.Year; year <= lastYear; year++)
        {
            var isDisposalYear = disposedOn?.Year == year;
            // Bought and sold in the same year: only the months actually held, whatever the first-year rule.
            var firstMonth = year == purchased.Year && (proRataFirstYear || isDisposalYear) ? purchased.Month : 1;
            var monthsInUse = isDisposalYear
                ? Math.Max(0, disposedOn!.Value.Month - firstMonth)
                : 12 - firstMonth + 1;

            var depreciation = bookValue * (ratePercent / 100m * monthsInUse / 12m);
            if (bookValue - depreciation < WriteOffThreshold && !isDisposalYear)
            {
                depreciation = bookValue;
            }

            var closing = bookValue - depreciation;
            Money? disposalResult = null;
            if (isDisposalYear)
            {
                disposalResult = (saleProceeds ?? Money.Zero) - closing;
                closing = Money.Zero;
            }

            years.Add(new DepreciationYear(year, bookValue, depreciation, closing, disposalResult));
            bookValue = closing;
        }

        return years;
    }
}
