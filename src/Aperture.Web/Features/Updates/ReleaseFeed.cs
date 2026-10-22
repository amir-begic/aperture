using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Aperture.Web.Installation;

namespace Aperture.Web.Features.Updates;

/// <summary>Where releases come from: set by the install command (aperture.json). Without it, Aperture doesn't look for updates.</summary>
public sealed class UpdateOptions
{
    /// <summary>The latest.json of the repository's latest release: …/releases/latest/download/latest.json.</summary>
    public string? FeedUrl { get; set; }

    public bool IsConfigured => Uri.TryCreate(FeedUrl, UriKind.Absolute, out var feed) && feed.Scheme == Uri.UriSchemeHttps;
}

/// <summary>
/// A release built by .github/workflows/release.yml and promoted by promote.yml, with its Mac package: downloaded from
/// <paramref name="DownloadUrl"/> and checked against <paramref name="Sha256"/>.
/// </summary>
public sealed record Release(
    ReleaseVersion Version, DateTimeOffset Published, string Notes, string Url, string AssetName, long AssetSize,
    Uri DownloadUrl, string Sha256);

/// <summary>
/// Reads the promoted release from the feed: latest.json, which .github/workflows/release.yml attaches to each release
/// next to the package it describes; promote.yml makes one release the latest. Nothing to sign in to; the SHA-256 in it
/// is checked before a download is installed.
/// </summary>
public sealed partial class ReleaseFeed(HttpClient http, UpdateOptions options)
{
    public static void Configure(HttpClient http)
    {
        http.Timeout = TimeSpan.FromMinutes(5);
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Aperture", "1.0"));
    }

    /// <summary>The promoted release, or null when nothing has been promoted yet.</summary>
    public async Task<Release?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, options.FeedUrl);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null; // Nothing promoted yet.
        }
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<FeedJson>(cancellationToken);
        return json is null ? null : ToRelease(json, Platform.RuntimeIdentifier);
    }

    /// <summary>
    /// The release for <paramref name="runtime"/> (e.g. "win-x64"). Null unless the entry names a package for that
    /// runtime with an https address and its SHA-256.
    /// </summary>
    public static Release? ToRelease(FeedJson json, string runtime)
    {
        var package = json.Packages?.GetValueOrDefault(runtime);
        if (string.IsNullOrWhiteSpace(json.Version) || package is null
            || package.File is null || !IsPackageFor(package.File, runtime)
            || !Uri.TryCreate(package.Download, UriKind.Absolute, out var download) || download.Scheme != Uri.UriSchemeHttps
            || package.Sha256 is null || !Sha256Pattern().IsMatch(package.Sha256))
        {
            return null;
        }

        return new Release(ReleaseVersion.Parse(json.Version), json.Published ?? DateTimeOffset.MinValue,
            json.Notes?.Trim() ?? "", json.Page ?? "", package.File, package.Size ?? 0, download, package.Sha256.ToLowerInvariant());
    }

    public async Task DownloadAsync(Release release, Stream destination, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        await body.CopyToAsync(destination, cancellationToken);
    }

    /// <summary>
    /// Whether <paramref name="name"/> is a package deploy/publish.sh produces for <paramref name="runtime"/>:
    /// aperture-&lt;version&gt;-&lt;runtime&gt;.tar.gz. Checked again by the updater before it installs one.
    /// </summary>
    public static bool IsPackageFor(string name, string runtime) =>
        PackageName().IsMatch(name) && name.EndsWith($"-{runtime}.tar.gz", StringComparison.Ordinal);

    /// <summary>"2026.10.20-58-ed1231e" from "aperture-2026.10.20-58-ed1231e-osx-arm64.tar.gz".</summary>
    public static string VersionOf(string package) => PackageName().Match(package).Groups["version"].Value;

    [GeneratedRegex(@"^aperture-(?<version>[0-9A-Za-z.-]+)-(?<runtime>(osx|win|linux)-(arm64|x64))\.tar\.gz$")]
    private static partial Regex PackageName();

    [GeneratedRegex("^[0-9A-Fa-f]{64}$")]
    private static partial Regex Sha256Pattern();

    public sealed record FeedJson(
        [property: JsonPropertyName("version")] string? Version,
        [property: JsonPropertyName("published")] DateTimeOffset? Published,
        [property: JsonPropertyName("notes")] string? Notes,
        [property: JsonPropertyName("page")] string? Page,
        [property: JsonPropertyName("packages")] Dictionary<string, PackageJson>? Packages);

    /// <summary>One platform's package in the feed, keyed by its runtime ("osx-arm64", "win-x64", "linux-x64").</summary>
    public sealed record PackageJson(
        [property: JsonPropertyName("file")] string? File,
        [property: JsonPropertyName("size")] long? Size,
        [property: JsonPropertyName("sha256")] string? Sha256,
        [property: JsonPropertyName("download")] string? Download);
}
