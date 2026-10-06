using System.Globalization;

namespace Funk.Web.Domain;

/// <summary>
/// An amount in Swiss francs, stored as whole Rappen (1 CHF = 100 Rappen).
/// Integer storage keeps sums exact and lets SQLite filter and sort by amount,
/// which it cannot do for EF Core's TEXT-backed <see cref="decimal"/>.
/// Totals are computed in C# with <see cref="MoneyExtensions.Sum(IEnumerable{Money})"/>:
/// LINQ's <c>Sum</c> only accepts plain number types.
/// </summary>
public readonly record struct Money(long Rappen) : IComparable<Money>
{
    private static readonly NumberFormatInfo SwissFormat = new()
    {
        NumberGroupSeparator = "'",
        NumberDecimalSeparator = ".",
        NegativeSign = "-",
    };

    public static readonly Money Zero = new(0);

    public decimal Chf => Rappen / 100m;

    public static Money FromChf(decimal chf) =>
        new((long)decimal.Round(chf * 100m, MidpointRounding.AwayFromZero));

    /// <summary>Swiss cash rounding to the nearest 0.05 CHF, as used on invoice totals.</summary>
    public Money RoundToFiveRappen() =>
        new((long)decimal.Round(Rappen / 5m, MidpointRounding.AwayFromZero) * 5);

    public static Money operator +(Money a, Money b) => new(a.Rappen + b.Rappen);
    public static Money operator -(Money a, Money b) => new(a.Rappen - b.Rappen);
    public static Money operator -(Money a) => new(-a.Rappen);
    public static Money operator *(Money a, int factor) => new(a.Rappen * factor);
    public static Money operator *(Money a, decimal factor) => FromChf(a.Chf * factor);

    public static bool operator <(Money a, Money b) => a.Rappen < b.Rappen;
    public static bool operator >(Money a, Money b) => a.Rappen > b.Rappen;
    public static bool operator <=(Money a, Money b) => a.Rappen <= b.Rappen;
    public static bool operator >=(Money a, Money b) => a.Rappen >= b.Rappen;

    public int CompareTo(Money other) => Rappen.CompareTo(other.Rappen);

    /// <summary>Formats as <c>CHF 1'234.50</c>, independent of the server's culture.</summary>
    public override string ToString() => $"CHF {Chf.ToString("#,##0.00", SwissFormat)}";
}

public static class MoneyExtensions
{
    public static Money Sum(this IEnumerable<Money> amounts) => new(amounts.Sum(m => m.Rappen));

    public static Money Sum<T>(this IEnumerable<T> items, Func<T, Money> amount) => items.Select(amount).Sum();
}
