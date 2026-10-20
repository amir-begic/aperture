using System.Globalization;
using System.Text.RegularExpressions;

namespace Funk.Web.Features.Updates;

/// <summary>
/// A version as deploy/publish.sh writes it: "2026.10.20-58-ed1231e", optionally "-dirty" (built with uncommitted
/// changes). The build number counts the commits, so a higher one is newer. Older versions without a build number
/// ("2026.10.06-b0d6009") and local builds ("dev") count as build 0.
/// </summary>
public sealed partial record ReleaseVersion(string Text, DateOnly? Date, int Build, string? Commit)
{
    public static ReleaseVersion Parse(string? text)
    {
        text = text?.Trim().TrimStart('v') ?? "";
        var match = Pattern().Match(text);
        if (!match.Success)
        {
            return new ReleaseVersion(text.Length > 0 ? text : "dev", null, 0, null);
        }

        var date = DateOnly.TryParseExact(match.Groups["date"].Value, "yyyy.MM.dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : (DateOnly?)null;
        var build = match.Groups["build"].Success ? int.Parse(match.Groups["build"].Value, CultureInfo.InvariantCulture) : 0;
        return new ReleaseVersion(text, date, build, match.Groups["commit"].Value);
    }

    public bool IsNewerThan(ReleaseVersion other) => Build > other.Build;

    /// <summary>"Build 58 · 20.10.2026", or the raw text for versions without a build number.</summary>
    public string Display => Build > 0 && Date is { } date ? $"build {Build} · {date:dd.MM.yyyy}" : Text;

    [GeneratedRegex(@"^(?<date>\d{4}\.\d{2}\.\d{2})-(?:(?<build>\d+)-)?(?<commit>[0-9a-f]{7,40})(?:-dirty)?$")]
    private static partial Regex Pattern();
}
