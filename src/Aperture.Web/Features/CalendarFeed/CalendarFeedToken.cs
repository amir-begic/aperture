using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Aperture.Web.Features.CalendarFeed;

/// <summary>
/// The secret in the calendar feed's URL. Calendar apps can't sign in, so the URL itself is the key:
/// 256 random bits, URL-safe. Making a new one revokes the old link.
/// </summary>
public static class CalendarFeedToken
{
    public const int MaxLength = 64;

    public static string New() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    /// <summary>Constant-time comparison, so response times don't reveal how much of a guess was right.</summary>
    public static bool Matches(string? stored, string? given) =>
        !string.IsNullOrEmpty(stored)
        && !string.IsNullOrEmpty(given)
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(stored), Encoding.UTF8.GetBytes(given));
}
