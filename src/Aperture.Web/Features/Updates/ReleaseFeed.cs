using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Aperture.Web.Features.Updates;

/// <summary>
/// Reads releases from a public feed, so a laptop installation needs no GitHub token: latest.json, written by
/// .github/workflows/promote.yml next to the package it describes. Nothing to sign in to; the SHA-256 in it is
/// checked before a download is installed.
/// </summary>
public sealed partial class ReleaseFeed(HttpClient http, UpdateOptions options) : IReleaseSource
{
    public static void Configure(HttpClient http)
    {
        http.Timeout = TimeSpan.FromMinutes(5);
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Aperture", "1.0"));
    }

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
        return json is null ? null : ToRelease(json);
    }

    /// <summary>Null unless the entry names a Mac package with an https address and its SHA-256.</summary>
    public static Release? ToRelease(FeedJson json)
    {
        if (string.IsNullOrWhiteSpace(json.Version)
            || json.File is null || !GitHubReleases.PackageName().IsMatch(json.File)
            || !Uri.TryCreate(json.Download, UriKind.Absolute, out var download) || download.Scheme != Uri.UriSchemeHttps
            || json.Sha256 is null || !Sha256Pattern().IsMatch(json.Sha256))
        {
            return null;
        }

        return new Release(ReleaseVersion.Parse(json.Version), json.Published ?? DateTimeOffset.MinValue,
            json.Notes?.Trim() ?? "", json.Page ?? "", 0, json.File, json.Size ?? 0, download, json.Sha256.ToLowerInvariant());
    }

    public async Task DownloadAsync(Release release, Stream destination, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        await body.CopyToAsync(destination, cancellationToken);
    }

    [GeneratedRegex("^[0-9A-Fa-f]{64}$")]
    private static partial Regex Sha256Pattern();

    public sealed record FeedJson(
        [property: JsonPropertyName("version")] string? Version,
        [property: JsonPropertyName("published")] DateTimeOffset? Published,
        [property: JsonPropertyName("notes")] string? Notes,
        [property: JsonPropertyName("page")] string? Page,
        [property: JsonPropertyName("file")] string? File,
        [property: JsonPropertyName("size")] long? Size,
        [property: JsonPropertyName("sha256")] string? Sha256,
        [property: JsonPropertyName("download")] string? Download);
}
