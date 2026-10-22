using System.IO.Compression;

namespace Aperture.Web.Installation;

/// <summary>
/// "Aperture.Web install": sets up the release this program belongs to in the home folder, and the platform's services
/// for it. Run by every installer (the macOS package, the Windows setup, install.sh on Linux) after unpacking a release.
/// Safe to run again: data, logins and backups are kept, so it also repairs an installation or changes its settings.
/// </summary>
public sealed class Installer(ApertureHome home, Platform platform, CommandLog log)
{
    /// <summary>
    /// The release's own default for the update feed, written by deploy/publish.sh: the installers needn't know it.
    /// </summary>
    public const string FeedFile = "feed.url";

    public async Task<int> InstallAsync(string? feed, int? port, bool openBrowser)
    {
        platform.CheckHome(home);
        var release = Release();
        log.Write($"Installing {Path.GetFileName(release)} into {home.Root}");

        Directory.CreateDirectory(Path.Combine(home.Data, "updates"));
        Directory.CreateDirectory(home.Logs);
        Directory.CreateDirectory(home.Backups);
        if (!OperatingSystem.IsWindows())
        {
            // Receipts, invoices, the login and its keys: this user's only. (Windows: the user profile already is.)
            File.SetUnixFileMode(home.Data, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.SetUnixFileMode(home.Backups, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        BackupJob.WriteDefaultConfig(home);

        var existing = HomeSettings.Read(home);
        feed ??= existing?.FeedUrl ?? ReadFeedFile(release);
        if (feed is not null && !(Uri.TryCreate(feed, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps))
        {
            throw new InstallationException($"The update feed must be an https address: {feed}");
        }

        log.Write("Starting Aperture");
        // Stopped first, so the port it listens on counts as free.
        await platform.StopWebAsync(home);
        HomeSettings settings;
        try
        {
            settings = new HomeSettings(Ports.Choose(port, existing?.Port, log), feed);
        }
        catch (InstallationException) when (existing is not null)
        {
            await platform.StartWebAsync(home); // unchanged, as it was
            throw;
        }
        settings.Write(home);
        await platform.LinkAppAsync(home, release);
        await platform.RegisterAsync(home);
        await platform.StartWebAsync(home);
        if (!await Health.WaitAsync(settings.Port))
        {
            log.Tail(home);
            log.Write("Aperture didn't start. See the log above.");
            return 1;
        }

        await platform.StartBackupsAsync(home);
        log.Write($"Aperture is running: {settings.Url}");
        log.Write(feed is null ? "No update feed: Aperture won't look for new versions." : $"Updates from: {feed}");
        log.Write($"Data: {home.Data}. Backups: {string.Join(", ", new BackupJob(home, TimeProvider.System, log).Folders())}");
        if (openBrowser)
        {
            try
            {
                platform.OpenBrowser(settings.Url);
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or AggregateException)
            {
                log.Write($"Open {settings.Url} in your browser.");
            }
        }

        return 0;
    }

    /// <summary>
    /// Stops Aperture and removes its services and program. Data, logs and backups stay, unless
    /// <paramref name="removeData"/>: then everything Aperture keeps in the install folder goes too (database, files,
    /// login, backups, logs, settings). Only what Aperture creates there is deleted, and only in a folder that is an
    /// Aperture installation; backups in other folders (backup.conf) stay.
    /// </summary>
    public async Task<int> UninstallAsync(bool removeData = false)
    {
        if (removeData && !IsInstallation())
        {
            throw new InstallationException($"{home.Root} isn't an Aperture installation, so nothing was removed.");
        }

        var otherBackups = removeData
            ? new BackupJob(home, TimeProvider.System, log).Folders()
                .Where(f => Path.GetFullPath(f) != Path.GetFullPath(home.Backups) && Directory.Exists(f)).ToList()
            : [];

        log.Write("Stopping Aperture");
        await platform.StopWebAsync(home);
        await platform.UnregisterAsync(home);
        // This program usually runs as app/Aperture.Web, so the release it runs from is the one app/ points at.
        var running = new[] { Platform.CurrentRelease(home), Path.GetDirectoryName(Environment.ProcessPath) };
        var app = new DirectoryInfo(home.App);
        if (app.LinkTarget is not null)
        {
            app.Delete();
        }

        // Windows can't delete the release this program runs from while it runs; the setup's uninstaller does, after.
        if (Directory.Exists(home.Releases))
        {
            foreach (var release in Directory.GetDirectories(home.Releases))
            {
                try
                {
                    Directory.Delete(release, recursive: true);
                }
                catch (Exception e) when ((e is IOException or UnauthorizedAccessException)
                    && running.Contains(release, StringComparer.OrdinalIgnoreCase))
                {
                    log.Write($"{Path.GetFileName(release)} is in use; it's removed after this.");
                }
            }

            if (!Directory.EnumerateFileSystemEntries(home.Releases).Any())
            {
                Directory.Delete(home.Releases);
            }
        }
        File.Delete(home.SettingsFile);
        if (!removeData)
        {
            log.Write($"Removed. Your data is still in {home.Data}, the backups in {home.Backups}.");
            return 0;
        }

        foreach (var folder in new[] { home.Data, home.Backups, home.Logs })
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        File.Delete(home.BackupConfig);
        if (Directory.Exists(home.Root) && !Directory.EnumerateFileSystemEntries(home.Root).Any())
        {
            Directory.Delete(home.Root);
        }

        // Written to the console only: the logs folder is gone.
        Console.WriteLine("Removed Aperture and all its data.");
        foreach (var folder in otherBackups)
        {
            Console.WriteLine($"Backups in {Path.Combine(folder, BackupJob.FolderName)} stay; delete them yourself if you like.");
        }
        return 0;
    }

    /// <summary>
    /// Whether the home folder is an Aperture installation, before deleting what's in it: never the user's own folder
    /// or a drive's root, and only one Aperture set up (or left its data in).
    /// </summary>
    private bool IsInstallation()
    {
        var root = Path.TrimEndingDirectorySeparator(home.Root);
        var user = Path.TrimEndingDirectorySeparator(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (Path.GetPathRoot(root) == home.Root || string.Equals(root, user, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return File.Exists(home.SettingsFile) || Directory.Exists(home.Releases) || new DirectoryInfo(home.App).LinkTarget is not null
            || File.Exists(Path.Combine(home.Data, "aperture.db"));
    }

    /// <summary>
    /// Puts a backup in place of the current data: a ZIP from Settings → Backups → Download a backup (or its unpacked
    /// folder), or a daily copy (…/Aperture Backup/database/aperture-YYYY-MM-DD.db, with the attachments next to it).
    /// The current database is kept in backups/before-restore first.
    /// </summary>
    public async Task<int> RestoreAsync(string source)
    {
        source = Path.GetFullPath(source);
        var unpacked = (string?)null;
        try
        {
            string database, attachments;
            if (File.Exists(source) && source.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                unpacked = Path.Combine(Path.GetTempPath(), $"aperture-restore-{Guid.NewGuid():N}");
                ZipFile.ExtractToDirectory(source, unpacked);
                database = Directory.GetFiles(unpacked, "aperture.db", SearchOption.AllDirectories).FirstOrDefault()
                    ?? throw new InstallationException($"{source} isn't an Aperture backup: it has no aperture.db.");
                attachments = Path.Combine(Path.GetDirectoryName(database)!, "attachments");
            }
            else if (Directory.Exists(source))
            {
                database = Path.Combine(source, "aperture.db");
                attachments = Path.Combine(source, "attachments");
            }
            else if (File.Exists(source) && source.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
            {
                database = source;
                attachments = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(source)!)!, "attachments");
            }
            else
            {
                throw new InstallationException($"Not a backup: {source}");
            }

            if (!File.Exists(database))
            {
                throw new InstallationException($"No aperture.db in {source}.");
            }
            if (DatabaseCopy.IntegrityCheck(database) is var integrity and not "ok")
            {
                throw new InstallationException($"The backup's database is damaged: {integrity}");
            }

            log.Write("Stopping Aperture");
            await platform.StopWebAsync(home);
            var live = Path.Combine(home.Data, "aperture.db");
            if (File.Exists(live))
            {
                var kept = Path.Combine(home.Backups, "before-restore", $"aperture-{DateTime.Now:yyyyMMdd-HHmmss}.db");
                DatabaseCopy.Copy(live, kept);
                log.Write($"The database as it was: {kept}");
            }

            DatabaseCopy.Restore(database, live);
            if (Directory.Exists(attachments))
            {
                foreach (var file in Directory.EnumerateFiles(attachments, "*", SearchOption.AllDirectories))
                {
                    var target = Path.Combine(home.Data, "attachments", Path.GetRelativePath(attachments, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target, overwrite: true);
                }
            }

            log.Write("Starting Aperture");
            await platform.StartWebAsync(home);
            var port = HomeSettings.Read(home)?.Port ?? ApertureHome.DefaultPort;
            if (!await Health.WaitAsync(port))
            {
                log.Tail(home);
                return 1;
            }

            log.Write($"Restored from {source}. Sign in at http://localhost:{port}.");
            return 0;
        }
        finally
        {
            if (unpacked is not null)
            {
                Directory.Delete(unpacked, recursive: true);
            }
        }
    }

    /// <summary>The release this program runs from, in releases/ (copied there if it was unpacked elsewhere).</summary>
    private string Release()
    {
        var running = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        if (string.Equals(Path.GetDirectoryName(running), home.Releases, StringComparison.OrdinalIgnoreCase))
        {
            return running;
        }

        var release = Path.Combine(home.Releases, Path.GetFileName(running));
        if (Directory.Exists(release))
        {
            if (string.Equals(release, Platform.CurrentRelease(home), StringComparison.OrdinalIgnoreCase))
            {
                return release; // The same version again: its copy is already in place.
            }
            Directory.Delete(release, recursive: true);
        }

        foreach (var file in Directory.EnumerateFiles(running, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(release, Path.GetRelativePath(running, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(target, File.GetUnixFileMode(file));
            }
        }

        return release;
    }

    private static string? ReadFeedFile(string release) =>
        File.Exists(Path.Combine(release, FeedFile)) && File.ReadAllText(Path.Combine(release, FeedFile)).Trim() is { Length: > 0 } feed
            ? feed
            : null;
}
