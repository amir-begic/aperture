using System.Net;
using System.Text;
using Aperture.Web.Configuration;
using Aperture.Web.Features.Dashboard;
using Aperture.Web.Features.Updates;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aperture.Tests.Features;

public class UpdateTests : IAsyncLifetime, IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly string _dataDirectory = Directory.CreateTempSubdirectory("aperture-updates-").FullName;

    public Task InitializeAsync() => _db.InitializeAsync();

    public Task DisposeAsync() => _db.DisposeAsync();

    public void Dispose() => Directory.Delete(_dataDirectory, recursive: true);

    private const string Package = "aperture-2026.10.20-58-ed1231e-osx-arm64.tar.gz";
    private static readonly byte[] PackageBytes = Encoding.UTF8.GetBytes("pretend this is a tarball");

    // The public feed (.github/workflows/promote.yml writes it).
    private const string FeedUrl = "https://github.example.test/me/aperture-releases/releases/latest/download/latest.json";
    private const string DownloadUrl = "https://github.example.test/me/aperture-releases/releases/download/v2026.10.20-58-ed1231e/" + Package;
    private static readonly string PackageSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(PackageBytes));

    private static string FeedJson(string? sha256 = null, string download = DownloadUrl, string file = Package, long? size = null) => $$"""
        {
          "version": "2026.10.20-58-ed1231e",
          "published": "2026-10-20T08:00:00Z",
          "notes": "- Add the laptop installer",
          "page": "https://github.example.test/me/aperture-releases/releases/tag/v2026.10.20-58-ed1231e",
          "file": "{{file}}",
          "size": {{size ?? PackageBytes.Length}},
          "sha256": "{{sha256 ?? PackageSha256}}",
          "download": "{{download}}"
        }
        """;

    /// <summary>Answers like the feed and its download; remembers what was asked.</summary>
    private sealed class FakeFeed(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var http = new HttpClient(handler, disposeHandler: false);
            ReleaseFeed.Configure(http);
            return http;
        }
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static Func<HttpRequestMessage, HttpResponseMessage> FeedLike(string json) => request =>
        request.RequestUri!.ToString() switch
        {
            FeedUrl => Json(json),
            DownloadUrl => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(PackageBytes) },
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };

    private (UpdateService Service, FakeFeed Feed, AperturePaths Paths) Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond, string? feedUrl = FeedUrl)
    {
        var options = new UpdateOptions { FeedUrl = feedUrl };
        var feed = new FakeFeed(respond);
        var paths = new AperturePaths(_dataDirectory);
        var service = new UpdateService(new Factory(feed), options, paths,
            new FixedTimeProvider(new DateTime(2026, 10, 20, 12, 0, 0)), NullLogger<UpdateService>.Instance);
        return (service, feed, paths);
    }

    [Theory]
    [InlineData("2026.10.20-58-ed1231e", 58, "ed1231e")]
    [InlineData("v2026.10.20-58-ed1231e", 58, "ed1231e")]
    [InlineData("2026.10.20-58-ed1231e-dirty", 58, "ed1231e")]
    [InlineData("2026.10.06-b0d6009", 0, "b0d6009")]
    [InlineData("dev", 0, null)]
    [InlineData("", 0, null)]
    public void Parses_versions_with_and_without_build_number(string text, int build, string? commit)
    {
        var version = ReleaseVersion.Parse(text);
        Assert.Equal(build, version.Build);
        Assert.Equal(commit, version.Commit);
    }

    [Fact]
    public void Higher_build_number_is_newer()
    {
        var current = ReleaseVersion.Parse("2026.10.20-58-ed1231e");
        Assert.True(ReleaseVersion.Parse("2026.10.20-59-aaaaaaa").IsNewerThan(current));
        Assert.False(ReleaseVersion.Parse("2026.10.21-58-bbbbbbb").IsNewerThan(current));
        Assert.False(ReleaseVersion.Parse("2026.10.19-57-ccccccc").IsNewerThan(current));
        Assert.True(current.IsNewerThan(ReleaseVersion.Parse("2026.10.06-b0d6009")));
        Assert.True(current.IsNewerThan(ReleaseVersion.Parse("dev")));
        Assert.Equal("build 58 · 20.10.2026", current.Display);
    }

    [Fact]
    public async Task Check_reads_the_promoted_release_without_signing_in()
    {
        var (updates, feed, _) = Create(FeedLike(FeedJson()));

        await updates.CheckAsync();

        Assert.True(updates.IsConfigured);
        var latest = Assert.IsType<Release>(updates.Latest);
        Assert.Equal(58, latest.Version.Build);
        Assert.Equal(Package, latest.AssetName);
        Assert.Equal(PackageSha256, latest.Sha256);
        Assert.Contains("laptop installer", latest.Notes);
        Assert.Same(latest, updates.Available); // the test build is "dev", so anything is newer
        Assert.Null(updates.Problem);
        Assert.NotNull(updates.LastCheck);
        var request = Assert.Single(feed.Requests);
        Assert.Equal(FeedUrl, request.RequestUri!.ToString());
        Assert.Null(request.Headers.Authorization);
        Assert.Contains("Aperture", request.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task Nothing_promoted_yet_is_not_a_problem()
    {
        var (updates, _, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        await updates.CheckAsync();
        Assert.Null(updates.Latest);
        Assert.Null(updates.Problem);
    }

    [Fact]
    public async Task Unreachable_feed_explains_the_problem()
    {
        var (updates, _, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        await updates.CheckAsync();
        Assert.Contains("Couldn't reach the update server", updates.Problem);
        Assert.Null(updates.Available);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://github.example.test/latest.json")]
    [InlineData("latest.json")]
    public async Task Without_an_https_feed_nothing_is_checked(string? feedUrl)
    {
        var (updates, feed, _) = Create(FeedLike(FeedJson()), feedUrl);
        await updates.CheckAsync();
        Assert.False(updates.IsConfigured);
        Assert.Empty(feed.Requests);
    }

    [Fact]
    public async Task Install_checks_the_download_and_asks_the_updater()
    {
        var (updates, feed, paths) = Create(FeedLike(FeedJson()));
        Directory.CreateDirectory(paths.UpdatesDirectory);
        await File.WriteAllTextAsync(Path.Combine(paths.UpdatesDirectory, "aperture-old-osx-arm64.tar.gz"), "old");
        await updates.CheckAsync();

        await updates.InstallAsync();

        Assert.Equal(PackageBytes, await File.ReadAllBytesAsync(Path.Combine(paths.UpdatesDirectory, Package)));
        Assert.Equal(Package + "\n", await File.ReadAllTextAsync(paths.UpdateRequestFile));
        Assert.True(updates.IsRequestPending);
        Assert.False(updates.IsDownloading);
        Assert.False(File.Exists(Path.Combine(paths.UpdatesDirectory, "aperture-old-osx-arm64.tar.gz")));
        Assert.Empty(Directory.GetFiles(paths.UpdatesDirectory, "*.part"));
        Assert.Equal(DownloadUrl, feed.Requests.Last().RequestUri!.ToString());
    }

    [Fact]
    public async Task Incomplete_download_is_not_handed_over()
    {
        var (updates, _, paths) = Create(FeedLike(FeedJson(size: 9999)));
        await updates.CheckAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => updates.InstallAsync());

        Assert.False(File.Exists(paths.UpdateRequestFile));
        Assert.Empty(Directory.GetFiles(paths.UpdatesDirectory));
    }

    [Fact]
    public async Task Download_that_doesnt_match_is_not_handed_over()
    {
        var (updates, _, paths) = Create(FeedLike(FeedJson(sha256: new string('a', 64))));
        await updates.CheckAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => updates.InstallAsync());

        Assert.Contains("doesn't match", error.Message);
        Assert.False(File.Exists(paths.UpdateRequestFile));
        Assert.Empty(Directory.GetFiles(paths.UpdatesDirectory));
    }

    [Theory]
    [InlineData("not-a-hash", DownloadUrl, Package)]
    [InlineData(null, "http://github.example.test/" + Package, Package)]
    [InlineData(null, DownloadUrl, "aperture-../../x-osx-arm64.tar.gz")]
    [InlineData(null, DownloadUrl, "something-else.zip")]
    public async Task Feed_entry_that_isnt_safe_to_install_is_ignored(string? sha256, string download, string file)
    {
        var (updates, _, paths) = Create(FeedLike(FeedJson(sha256, download, file)));
        await updates.CheckAsync();
        await updates.InstallAsync();
        Assert.Null(updates.Latest);
        Assert.False(File.Exists(paths.UpdateRequestFile));
    }

    [Fact]
    public void Reads_the_updaters_status()
    {
        var paths = new AperturePaths(_dataDirectory);
        Directory.CreateDirectory(paths.UpdatesDirectory);
        File.WriteAllText(paths.UpdateStatusFile,
            """{"state":"failed","version":"2026.10.21-59-abc1234","message":"Update failed; 2026.10.20-58-ed1231e is running again.","time":"2026-10-20T21:48:10Z"}""");

        var status = Assert.IsType<UpdateStatus>(UpdateStatus.Read(paths));
        Assert.True(status.Failed);
        Assert.Equal("2026.10.21-59-abc1234", status.Version);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 21, 48, 10, TimeSpan.Zero), status.Time);

        File.WriteAllText(paths.UpdateStatusFile, "{ half");
        Assert.Null(UpdateStatus.Read(paths));
    }

    [Fact]
    public async Task Dashboard_mentions_an_available_update_last()
    {
        var release = ReleaseFeed.ToRelease(System.Text.Json.JsonSerializer.Deserialize<ReleaseFeed.FeedJson>(FeedJson())!)!;
        await using var db = _db.CreateContext();

        var items = await Attention.ItemsAsync(db, new DateTime(2026, 10, 20, 12, 0, 0), update: release);

        var item = Assert.Single(items);
        Assert.Equal(AttentionLevel.Info, item.Level);
        Assert.Equal("Aperture build 58 · 20.10.2026 is available", item.Text);
        Assert.Equal("settings#updates", item.Link);
    }
}
