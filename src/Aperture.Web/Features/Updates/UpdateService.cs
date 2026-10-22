using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aperture.Web.Configuration;
using Aperture.Web.Installation;

namespace Aperture.Web.Features.Updates;

/// <summary>What the updater ("Aperture.Web update", see Updater) reports about the last update.</summary>
public sealed record UpdateStatus(
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("time")] DateTimeOffset? Time)
{
    [JsonIgnore] public bool IsRunning => State == "running";
    [JsonIgnore] public bool Succeeded => State == "succeeded";
    [JsonIgnore] public bool Failed => State == "failed";

    public static UpdateStatus? Read(AperturePaths paths)
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
/// Looks for new releases in the public feed (see ReleaseFeed) and hands one over to the updater. Aperture can't replace itself while it runs:
/// it downloads the release into data/updates and writes its name to the request file, and the platform starts the
/// updater (Updater: stop, copy the database, switch, check, roll back if needed). launchd and systemd watch for the
/// file; on Windows Aperture starts the updater's task itself. Singleton: the dashboard and Settings share one state.
/// </summary>
public sealed class UpdateService(
    IHttpClientFactory httpFactory, UpdateOptions options, AperturePaths paths, ApertureHome home, Platform platform,
    TimeProvider time, ILogger<UpdateService> logger)
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
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            Problem = $"Couldn't reach the update server. Is {platform.ThisComputer} online?";
            logger.LogWarning(e, "Update check failed");
        }
        finally
        {
            LastCheck = time.GetUtcNow();
            _lock.Release();
        }

        Changed?.Invoke();
    }

    /// <summary>Downloads the available release and starts the updater. Aperture restarts shortly after.</summary>
    public async Task InstallAsync(CancellationToken cancellationToken = default)
    {
        if (Available is not { } release || !ReleaseFeed.IsPackageFor(release.AssetName, Platform.RuntimeIdentifier))
        {
            return;
        }

        await _lock.WaitAsync(cancellationToken);
        IsDownloading = true;
        Changed?.Invoke();
        try
        {
            Directory.CreateDirectory(paths.UpdatesDirectory);
            foreach (var old in Directory.EnumerateFiles(paths.UpdatesDirectory, "aperture-*"))
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
            if (!string.Equals(await Sha256Async(partial, cancellationToken), release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(partial);
                throw new InvalidOperationException("The download doesn't match the release. Try again.");
            }
            File.Move(partial, target, overwrite: true);

            // Written in one go (temp file, then rename), so the updater never sees half a name.
            var request = paths.UpdateRequestFile + ".tmp";
            await File.WriteAllTextAsync(request, release.AssetName + "\n", cancellationToken);
            File.Move(request, paths.UpdateRequestFile, overwrite: true);
            await platform.StartUpdaterAsync(home);
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

    private ReleaseFeed Client() => new(httpFactory.CreateClient(nameof(ReleaseFeed)), options);

    private static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellationToken));
    }
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
