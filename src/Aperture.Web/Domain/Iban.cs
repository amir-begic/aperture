using System.ComponentModel.DataAnnotations;

namespace Aperture.Web.Domain;

public static class Iban
{
    /// <summary>Removes spaces and upper-cases, e.g. "ch93 0076 2011..." becomes "CH9300762011...".</summary>
    public static string Normalize(string iban) =>
        string.Concat(iban.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();

    /// <summary>Checks length, characters and the ISO 13616 mod-97 checksum.</summary>
    public static bool IsValid(string iban)
    {
        var value = Normalize(iban);
        if (value.Length is < 15 or > 34 || !value.All(char.IsAsciiLetterOrDigit)
            || !char.IsAsciiLetter(value[0]) || !char.IsAsciiLetter(value[1]))
        {
            return false;
        }

        // Move country code and check digits to the end, map letters to 10..35,
        // and compute the remainder digit by digit to avoid big integers.
        var remainder = 0;
        foreach (var c in value[4..] + value[..4])
        {
            var digits = char.IsAsciiDigit(c) ? c - '0' : c - 'A' + 10;
            remainder = digits >= 10
                ? (remainder * 100 + digits) % 97
                : (remainder * 10 + digits) % 97;
        }

        return remainder == 1;
    }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class ValidIbanAttribute : ValidationAttribute
{
    public ValidIbanAttribute() : base("Not a valid IBAN.") { }

    // Empty values are left to [Required].
    public override bool IsValid(object? value) =>
        value is not string s || string.IsNullOrWhiteSpace(s) || Iban.IsValid(s);
}
