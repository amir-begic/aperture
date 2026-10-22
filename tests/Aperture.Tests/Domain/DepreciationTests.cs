using Aperture.Web.Domain;

namespace Aperture.Tests.Domain;

public class DepreciationTests
{
    private static Money Chf(decimal value) => Money.FromChf(value);

    [Fact]
    public void Bought_in_january_full_rate_on_the_remaining_book_value()
    {
        var schedule = Depreciation.Schedule(new DateOnly(2026, 1, 15), Chf(3000), 30, null, null, untilYear: 2028);

        Assert.Equal([2026, 2027, 2028], schedule.Select(y => y.Year));
        Assert.Equal(Chf(900), schedule[0].Depreciation);   // 30% of 3000
        Assert.Equal(Chf(2100), schedule[0].Closing);
        Assert.Equal(Chf(630), schedule[1].Depreciation);   // 30% of 2100
        Assert.Equal(Chf(1470), schedule[1].Closing);
        Assert.Equal(Chf(1470), schedule[2].Opening);
    }

    [Fact]
    public void Full_rate_in_the_year_of_purchase_by_default()
    {
        // St. Gallen: no pro rata, even when bought in October (StB 41 Nr. 1).
        var schedule = Depreciation.Schedule(new DateOnly(2026, 10, 6), Chf(2000), 30, null, null, untilYear: 2027);

        Assert.Equal(Chf(600), schedule[0].Depreciation);
        Assert.Equal(Chf(420), schedule[1].Depreciation);
    }

    [Fact]
    public void First_year_can_be_pro_rata_from_the_month_of_purchase()
    {
        // Bought in October: Oct, Nov, Dec = 3 months
        var schedule = Depreciation.Schedule(new DateOnly(2026, 10, 6), Chf(2400), 40, null, null, untilYear: 2027, proRataFirstYear: true);

        Assert.Equal(Chf(240), schedule[0].Depreciation);   // 2400 * 40% * 3/12
        Assert.Equal(Chf(864), schedule[1].Depreciation);   // 2160 * 40%
    }

    [Fact]
    public void Year_of_sale_is_pro_rata_after_a_full_first_year()
    {
        // Full-year convention: Jan–Mar before an April sale = 3 months.
        var schedule = Depreciation.Schedule(new DateOnly(2026, 10, 1), Chf(1200), 30, new DateOnly(2027, 4, 1), Chf(1000), untilYear: 2027);

        Assert.Equal(Chf(360), schedule[0].Depreciation);            // full year 2026
        Assert.Equal(Chf(63), schedule[1].Depreciation);             // 840 * 30% * 3/12
        Assert.Equal(Chf(223), schedule[1].DisposalResult);          // 1000 - 777
    }

    [Fact]
    public void Bought_and_sold_in_the_same_year_counts_only_the_months_held()
    {
        // Bought in October, sold in December: Oct + Nov = 2 months, not a full year.
        var schedule = Depreciation.Schedule(new DateOnly(2026, 10, 5), Chf(1200), 30, new DateOnly(2026, 12, 15), Chf(1000), untilYear: 2026);

        var year = Assert.Single(schedule);
        Assert.Equal(Chf(60), year.Depreciation);                    // 1200 * 30% * 2/12
        Assert.Equal(Chf(-140), year.DisposalResult);                // 1000 - 1140
    }

    [Fact]
    public void Selling_stops_depreciation_and_books_the_gain_or_loss()
    {
        // Bought Jan 2026 for 3000 at 30%; sold in April 2027 for 2000.
        var schedule = Depreciation.Schedule(new DateOnly(2026, 1, 1), Chf(3000), 30, new DateOnly(2027, 4, 20), Chf(2000), untilYear: 2030);

        Assert.Equal([2026, 2027], schedule.Select(y => y.Year));
        var saleYear = schedule[1];
        Assert.Equal(Chf(157.50m), saleYear.Depreciation);       // 2100 * 30% * 3/12 (Jan–Mar)
        Assert.Equal(Money.Zero, saleYear.Closing);
        Assert.Equal(Chf(57.50m), saleYear.DisposalResult);      // 2000 - 1942.50
    }

    [Fact]
    public void Tiny_remainders_are_written_off()
    {
        var schedule = Depreciation.Schedule(new DateOnly(2020, 1, 1), Chf(10), 45, null, null, untilYear: 2030);

        Assert.Equal(Money.Zero, schedule[^1].Closing);
        Assert.Equal(Chf(10), schedule.Sum(y => y.Depreciation));
    }

    [Fact]
    public void Nothing_before_the_purchase_year()
    {
        Assert.Empty(Depreciation.Schedule(new DateOnly(2027, 3, 1), Chf(1000), 30, null, null, untilYear: 2026));
    }
}
