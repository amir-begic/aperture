using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using Aperture.Web.Features.Updates;

namespace Aperture.Web.Installation;

/// <summary>
/// Installs a release ("Aperture.Web update"): one Aperture downloaded (Settings → Updates), started by the platform's
/// updater when the request file appears, or one given by hand. Stops Aperture, copies the database, switches app/ to
/// the new release and checks that it starts. If it doesn't, the previous release and the database copy come back.
/// Runs from the current release, which stays on disk, so it can switch away from it.
/// </summary>
public sealed class Updater(ApertureHome home, Platform platform, TimeProvider time, CommandLog log)
{
    /// <summary>The current release and this many older ones are kept, to go back to.</summary>
    public const int KeepReleases = 3;

    private string UpdatesDirectory => Path.Combine(home.Data, "updates");
    private string RequestFile => Path.Combine(UpdatesDirectory, "request");
    private string StatusFile => Path.Combine(UpdatesDirectory, "status.json");

    /// <summary>Installs <paramref name="package"/>, or the one Aperture asked for when null. 0 when it's running.</summary>
    public async Task<int> RunAsync(string? package)
    {
        string path;
        if (package is null)
        {
            // launchd and systemd also start the updater when the request file goes away; then there's nothing to do.
            if (!File.Exists(RequestFile))
            {
                return 0;
            }

            var name = File.ReadLines(RequestFile).FirstOrDefault()?.Trim() ?? "";
            File.Delete(RequestFile);
            // Only a release package lying in the updates folder, never a path from elsewhere.
            if (!ReleaseFeed.IsPackageFor(name, Platform.RuntimeIdentifier) || !File.Exists(Path.Combine(UpdatesDirectory, name)))
            {
                log.Write($"Ignored request: '{name}'");
                WriteStatus("failed", null, "The downloaded release wasn't found. Try again.");
                return 1;
            }

            path = Path.Combine(UpdatesDirectory, name);
        }
        else
        {
            path = Path.GetFullPath(package);
            if (!ReleaseFeed.IsPackageFor(Path.GetFileName(path), Platform.RuntimeIdentifier) || !File.Exists(path))
            {
                log.Write($"Not a release package for {Platform.RuntimeIdentifier}: {path}");
                return 1;
            }
        }

        var version = ReleaseFeed.VersionOf(Path.GetFileName(path));
        WriteStatus("running", version, "Installing");
        log.Write($"Updating to {version}");
        try
        {
            await InstallAsync(path);
            WriteStatus("succeeded", version, "Updated");
            log.Write($"Updated to {version}");
            return 0;
        }
        catch (Exception e) when (e is InstallationException or IOException or UnauthorizedAccessException
            or InvalidDataException or Microsoft.Data.Sqlite.SqliteException)
        {
            WriteStatus("failed", version, e.Message);
            log.Write($"Update to {version} failed: {e.Message}");
            return 1;
        }
        finally
        {
            if (package is null)
            {
                File.Delete(path);
            }
        }
    }

    private async Task InstallAsync(string package)
    {
        var previous = Platform.CurrentRelease(home);
        var release = Unpack(package, previous);
        await platform.PrepareReleaseAsync(release);
        var port = HomeSettings.Read(home)?.Port ?? ApertureHome.DefaultPort;
        log.Write($"New: {Path.GetFileName(release)}, old: {Path.GetFileName(previous) ?? "none"}");

        log.Write("Stopping Aperture");
        await platform.StopWebAsync(home);
        string? snapshot;
        try
        {
            snapshot = SnapshotDatabase(Path.GetFileName(release));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            await platform.StartWebAsync(home);
            throw new InstallationException($"Could not copy the database ({e.Message}), so nothing was changed. The old version is running again.");
        }

        log.Write("Starting the new version");
        await platform.LinkAppAsync(home, release);
        RotateLog();
        await platform.StartWebAsync(home);
        if (await Health.WaitAsync(port))
        {
            RemoveOldReleases(release, previous);
            return;
        }

        log.Write("The new version didn't start.");
        log.Tail(home);
        if (previous is null)
        {
            throw new InstallationException("The new version didn't start, and there's no previous version to go back to.");
        }

        log.Write($"Going back to {Path.GetFileName(previous)}");
        await platform.StopWebAsync(home);
        if (snapshot is not null)
        {
            DatabaseCopy.Restore(snapshot, Path.Combine(home.Data, "aperture.db"));
        }
        await platform.LinkAppAsync(home, previous);
        await platform.StartWebAsync(home);
        throw new InstallationException(await Health.WaitAsync(port)
            ? $"The new version didn't start; {Path.GetFileName(previous)} is running again with the database as it was before."
            : $"The new version didn't start, and the previous one didn't either. See {home.Log("aperture")}.");
    }

    /// <summary>Unpacks the package into releases/ and returns its folder. Package entries can't leave that folder.</summary>
    public string Unpack(string package, string? current)
    {
        Directory.CreateDirectory(home.Releases);
        var folder = TopFolder(package);
        var release = Path.Combine(home.Releases, folder);
        if (string.Equals(release, current, StringComparison.OrdinalIgnoreCase))
        {
            throw new InstallationException($"{folder} is already running.");
        }

        if (Directory.Exists(release))
        {
            Directory.Delete(release, recursive: true);
        }

        using (var file = File.OpenRead(package))
        using (var gzip = new GZipStream(file, CompressionMode.Decompress))
        {
            TarFile.ExtractToDirectory(gzip, home.Releases, overwriteFiles: true);
        }

        if (!File.Exists(platform.Executable(release)))
        {
            Directory.Delete(release, recursive: true);
            throw new InstallationException($"{Path.GetFileName(package)} doesn't contain Aperture for this computer.");
        }

        return release;
    }

    /// <summary>The single folder a release package holds, "aperture-&lt;version&gt;".</summary>
    private static string TopFolder(string package)
    {
        using var file = File.OpenRead(package);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        var folders = new HashSet<string>();
        while (tar.GetNextEntry() is { } entry)
        {
            folders.Add(entry.Name.Replace('\\', '/').TrimStart('.', '/').Split('/')[0]);
        }

        return folders.Count == 1 && folders.Single() is var folder && folder.StartsWith("aperture-") && !folder.Contains("..")
            ? folder
            : throw new InstallationException($"{Path.GetFileName(package)} doesn't look like an Aperture release.");
    }

    /// <summary>A copy of the database before anything changes it (the new version may migrate it). The last five stay.</summary>
    private string? SnapshotDatabase(string label)
    {
        var database = Path.Combine(home.Data, "aperture.db");
        if (!File.Exists(database))
        {
            return null;
        }

        var copy = Path.Combine(home.BeforeUpdateBackups, $"aperture-{time.GetLocalNow():yyyyMMdd-HHmmss}-{label}.db");
        DatabaseCopy.Copy(database, copy);
        foreach (var old in new DirectoryInfo(home.BeforeUpdateBackups).GetFiles("aperture-*.db")
                     .OrderByDescending(f => f.LastWriteTimeUtc).Skip(5))
        {
            old.Delete();
        }

        log.Write($"Database copy: {copy}");
        return copy;
    }

    private void RemoveOldReleases(string current, string? previous)
    {
        var running = Path.GetDirectoryName(Environment.ProcessPath);
        foreach (var old in new DirectoryInfo(home.Releases).GetDirectories("aperture-*")
                     .Where(d => !string.Equals(d.FullName, current, StringComparison.OrdinalIgnoreCase))
                     .OrderByDescending(d => d.LastWriteTimeUtc)
                     .Skip(KeepReleases - 1))
        {
            if (string.Equals(old.FullName, previous, StringComparison.OrdinalIgnoreCase)
                || string.Equals(old.FullName, running, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                old.Delete(recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log.Write($"Couldn't remove {old.Name}: {e.Message}");
            }
        }
    }

    /// <summary>Aperture reopens its log when it restarts, so an update is a good moment to start a new one.</summary>
    private void RotateLog()
    {
        var log = home.Log("aperture");
        if (!File.Exists(log))
        {
            return;
        }

        try
        {
            File.Move(log + ".2", log + ".3", overwrite: true);
        }
        catch (FileNotFoundException)
        {
        }
        try
        {
            File.Move(log + ".1", log + ".2", overwrite: true);
        }
        catch (FileNotFoundException)
        {
        }
        File.Move(log, log + ".1", overwrite: true);
    }

    /// <summary>What Settings → Updates shows (UpdateStatus).</summary>
    private void WriteStatus(string state, string? version, string message)
    {
        Directory.CreateDirectory(UpdatesDirectory);
        var temp = StatusFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new UpdateStatus(state, version, message, time.GetUtcNow())));
        File.Move(temp, StatusFile, overwrite: true);
    }
}
