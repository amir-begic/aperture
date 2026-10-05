using Funk.Web.Domain;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Funk.Web.Data.Converters;

/// <summary>Stores <see cref="Money"/> as an INTEGER column of Rappen.</summary>
public sealed class MoneyConverter() : ValueConverter<Money, long>(
    money => money.Rappen,
    rappen => new Money(rappen));
