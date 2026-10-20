using System.Text.Json;
using System.Text.Json.Serialization;
using Funk.Web.Configuration;

namespace Funk.Web.Features.Updates;

/// <summary>What the updater on the Mac (deploy/macos/auto-update.sh) reports about the last update.</summary>
public sealed record UpdateStatus(
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("time")] DateTimeOffset? Time)
{
    public bool IsRunning => State == "running";
    public bool Succeeded => State == "succeeded";
    public bool Failed => State == "failed";

    public static UpdateStatus? Read(FunkPaths paths)
    {
        try
        {
            using var file = File.OpenRead(paths.UpdateStatusFile);
            return JsonSerializer.Deserialize<UpdateStatus>(file);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// Looks for new releases on GitHub and hands one over to the updater. Funk can't replace itself while it runs:
/// it downloads the release into data/updates and writes its name to the request file; launchd sees the file
/// and starts deploy/macos/auto-update.sh, which runs update.sh (stop, copy the database, switch, check, roll back
/// if needed). Singleton: the dashboard and Settings share one state.
/// </summary>
public sealed class UpdateService(
    IHttpClientFactory httpFactory, UpdateOptions options, FunkPaths paths, TimeProvider time, ILogger<UpdateService> logger)
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public static ReleaseVersion Current { get; } = ReleaseVersion.Parse(AppVersion.Current);

    public bool IsConfigured => options.IsConfigured;

    public DateTimeOffset? LastCheck { get; private set; }

    public Release? Latest { get; private set; }

    public string? Problem { get; private set; }

    /// <summary>Set while a release downloads, until the updater takes over.</summary>
    public bool IsDownloading { get; private set; }

    /// <summary>The newest release, if it's newer than the running version.</summary>
    public Release? Available => Latest is { } latest && latest.Version.IsNewerThan(Current) ? latest : null;

    /// <summary>Raised after a check or a download; listeners re-render.</summary>
    public event Action? Changed;

    public UpdateStatus? Status => UpdateStatus.Read(paths);

    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return;
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            Latest = await Client().GetLatestAsync(cancellationToken);
            Problem = null;
        }
        catch (HttpRequestException e) when (e.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
        {
            Problem = "GitHub didn't accept the token. It may have expired: make a new one and run install.sh again.";
            logger.LogWarning(e, "Update check: token rejected");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            Problem = "Couldn't reach GitHub. Is the Mac online?";
            logger.LogWarning(e, "Update check failed");
        }
        finally
        {
            LastCheck = time.GetUtcNow();
            _lock.Release();
        }

        Changed?.Invoke();
    }

    /// <summary>Downloads the available release and starts the updater. Funk restarts shortly after.</summary>
    public async Task InstallAsync(CancellationToken cancellationToken = default)
    {
        if (Available is not { } release || !GitHubReleases.PackageName().IsMatch(release.AssetName))
        {
            return;
        }

        await _lock.WaitAsync(cancellationToken);
        IsDownloading = true;
        Changed?.Invoke();
        try
        {
            Directory.CreateDirectory(paths.UpdatesDirectory);
            foreach (var old in Directory.EnumerateFiles(paths.UpdatesDirectory, "funk-*"))
            {
                File.Delete(old);
            }

            var target = Path.Combine(paths.UpdatesDirectory, release.AssetName);
            var partial = target + ".part";
            await using (var file = File.Create(partial))
            {
                await Client().DownloadAsync(release, file, cancellationToken);
            }
            if (release.AssetSize > 0 && new FileInfo(partial).Length != release.AssetSize)
            {
                File.Delete(partial);
                throw new InvalidOperationException("The download is incomplete. Try again.");
            }
            File.Move(partial, target, overwrite: true);

            // Written in one go (temp file, then rename), so the updater never sees half a name.
            var request = paths.UpdateRequestFile + ".tmp";
            await File.WriteAllTextAsync(request, release.AssetName + "\n", cancellationToken);
            File.Move(request, paths.UpdateRequestFile, overwrite: true);
            logger.LogInformation("Update to {Version} handed to the updater", release.Version.Text);
        }
        finally
        {
            IsDownloading = false;
            _lock.Release();
            Changed?.Invoke();
        }
    }

    /// <summary>True while a request waits for the updater (it removes the file when it starts).</summary>
    public bool IsRequestPending => File.Exists(paths.UpdateRequestFile);

    private GitHubReleases Client() => new(httpFactory.CreateClient(nameof(GitHubReleases)), options);
}

/// <summary>Checks for updates a minute after start and then every six hours, so the dashboard can say so.</summary>
public sealed class UpdateCheckWorker(UpdateService updates) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!updates.IsConfigured)
        {
            return;
        }

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await updates.CheckAsync(stoppingToken);
                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
