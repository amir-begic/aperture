using System.Globalization;
using Funk.Web.Domain;

namespace Funk.Tests.Domain;

public class MoneyTests
{
    [Theory]
    [InlineData("12.34", 1234)]
    [InlineData("0.005", 1)]   // midpoint rounds away from zero
    [InlineData("-0.005", -1)]
    [InlineData("1500", 150000)]
    public void FromChf_converts_to_rappen(string chf, long expectedRappen)
    {
        Assert.Equal(expectedRappen, Money.FromChf(decimal.Parse(chf, CultureInfo.InvariantCulture)).Rappen);
    }

    [Theory]
    [InlineData(1230, 1230)]
    [InlineData(1232, 1230)]
    [InlineData(1233, 1235)]
    [InlineData(1237, 1235)]
    [InlineData(1238, 1240)]
    [InlineData(-1233, -1235)]
    public void RoundToFiveRappen_uses_swiss_cash_rounding(long rappen, long expected)
    {
        Assert.Equal(new Money(expected), new Money(rappen).RoundToFiveRappen());
    }

    [Fact]
    public void Arithmetic_is_exact()
    {
        var total = Money.FromChf(0.10m) + Money.FromChf(0.20m);

        Assert.Equal(Money.FromChf(0.30m), total);
        Assert.Equal(Money.FromChf(0.90m), total * 3);
        Assert.Equal(Money.FromChf(-0.30m), -total);
        Assert.Equal(Money.Zero, total - total);
    }

    [Fact]
    public void Multiplying_by_a_rate_rounds_to_rappen()
    {
        // 8.1% VAT on CHF 1'234.55 = 99.99855
        Assert.Equal(Money.FromChf(100.00m), Money.FromChf(1234.55m) * 0.081m);
    }

    [Theory]
    [InlineData(0, "CHF 0.00")]
    [InlineData(5, "CHF 0.05")]
    [InlineData(123450, "CHF 1'234.50")]
    [InlineData(123456789, "CHF 1'234'567.89")]
    [InlineData(-123450, "CHF -1'234.50")]
    public void ToString_uses_swiss_format(long rappen, string expected)
    {
        Assert.Equal(expected, new Money(rappen).ToString());
    }

    [Fact]
    public void Comparison_operators_compare_amounts()
    {
        Assert.True(Money.FromChf(1) < Money.FromChf(2));
        Assert.True(Money.FromChf(2) >= Money.FromChf(2));
        Assert.Equal(-1, Money.FromChf(1).CompareTo(Money.FromChf(2)));
    }
}
