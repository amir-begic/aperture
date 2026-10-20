using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Funk.Web.Features.Updates;

/// <summary>Where releases come from. Set by install.sh on the Mac mini; without them, Funk doesn't look for updates.</summary>
public sealed class UpdateOptions
{
    /// <summary>"owner/repository" on GitHub.</summary>
    public string? Repository { get; set; }

    /// <summary>Fine-grained token with read access to that repository's contents (it's private).</summary>
    public string? GitHubToken { get; set; }

    /// <summary>Only changed for tests.</summary>
    public string ApiUrl { get; set; } = "https://api.github.com/";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Repository) && Regex.IsMatch(Repository, "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")
        && !string.IsNullOrWhiteSpace(GitHubToken);
}

/// <summary>A release built by .github/workflows/release.yml, with its Mac mini package.</summary>
public sealed record Release(ReleaseVersion Version, DateTimeOffset Published, string Notes, string Url, long AssetId, string AssetName, long AssetSize);

/// <summary>Reads releases from the GitHub API.</summary>
public sealed partial class GitHubReleases(HttpClient http, UpdateOptions options)
{
    public static void Configure(HttpClient http, UpdateOptions options)
    {
        http.BaseAddress = new Uri(options.ApiUrl);
        http.Timeout = TimeSpan.FromMinutes(5);
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Funk", "1.0"));
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrWhiteSpace(options.GitHubToken))
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.GitHubToken);
        }
    }

    /// <summary>The latest published release (not drafts or pre-releases), or null when there's none with a Mac package.</summary>
    public async Task<Release?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"repos/{options.Repository}/releases/latest");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null; // No release yet (or the token can't see the repository: GitHub says 404 for both).
        }
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<ReleaseJson>(cancellationToken);
        return json is null ? null : ToRelease(json);
    }

    /// <summary>Picks the Mac mini package; null if the release has none.</summary>
    public static Release? ToRelease(ReleaseJson json)
    {
        var asset = json.Assets?.FirstOrDefault(a => PackageName().IsMatch(a.Name ?? ""));
        if (asset is null || json.TagName is null)
        {
            return null;
        }

        return new Release(ReleaseVersion.Parse(json.TagName), json.PublishedAt ?? DateTimeOffset.MinValue,
            json.Body?.Trim() ?? "", json.HtmlUrl ?? "", asset.Id, asset.Name!, asset.Size);
    }

    /// <summary>Downloads the package into <paramref name="destination"/>.</summary>
    public async Task DownloadAsync(Release release, Stream destination, CancellationToken cancellationToken = default)
    {
        // The asset API answers with a redirect to the file; HttpClient follows it and doesn't pass the token on.
        using var request = new HttpRequestMessage(HttpMethod.Get, $"repos/{options.Repository}/releases/assets/{release.AssetId}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        await body.CopyToAsync(destination, cancellationToken);
    }

    /// <summary>The package name deploy/publish.sh produces. Also checked again by the updater on the Mac.</summary>
    [GeneratedRegex(@"^funk-[0-9A-Za-z.-]+-osx-arm64\.tar\.gz$")]
    public static partial Regex PackageName();

    public sealed record ReleaseJson(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("assets")] List<AssetJson>? Assets);

    public sealed record AssetJson(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("size")] long Size);
}
