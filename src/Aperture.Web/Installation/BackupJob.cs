using System.Globalization;
using System.Text.Json;
using Aperture.Web.Features.Backup;

namespace Aperture.Web.Installation;

/// <summary>
/// The daily backup ("Aperture.Web backup"; scheduled by the install command). For each folder listed in backup.conf
/// that exists (the home's backups/ folder, and any external drive added there), writes to "&lt;folder&gt;/Aperture Backup":
/// <list type="bullet">
/// <item>database/aperture-YYYY-MM-DD.db: a consistent copy, checked before it's copied anywhere. Kept for
/// <see cref="KeepDays"/> days; the one from the 1st of each month forever.</item>
/// <item>attachments/: all uploaded files (receipts, payslips, invoices); added, never deleted.</item>
/// <item>keys/: the login and the keys that sign cookies and notifications.</item>
/// </list>
/// The result goes to data/backup-status.json, which the app shows in Settings and on the Dashboard. It runs outside the
/// app, so backups still happen when the app itself is broken.
/// </summary>
public sealed class BackupJob(ApertureHome home, TimeProvider time, CommandLog log)
{
    public const int KeepDays = 90;
    public const string FolderName = "Aperture Backup";

    private string Database => Path.Combine(home.Data, "aperture.db");
    private string StatusFile => Path.Combine(home.Data, "backup-status.json");

    /// <summary>0 if at least one folder got a backup.</summary>
    public int Run()
    {
        var started = time.GetUtcNow();
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var destinations = new List<string>();
        var problems = new List<string>();
        log.Write("Backup started");

        var folders = Folders();
        if (!File.Exists(Database))
        {
            problems.Add($"No database found in {home.Data}");
        }
        else if (folders.Count == 0)
        {
            problems.Add($"No backup folders configured in {home.BackupConfig}");
        }
        else
        {
            var snapshot = Path.Combine(Path.GetTempPath(), $"aperture-backup-{Guid.NewGuid():N}.db");
            try
            {
                DatabaseCopy.Copy(Database, snapshot);
                var integrity = DatabaseCopy.IntegrityCheck(snapshot);
                if (integrity != "ok")
                {
                    problems.Add($"Database integrity check failed: {integrity}");
                }
                else
                {
                    foreach (var folder in folders)
                    {
                        BackUpTo(folder, snapshot, today, destinations, problems);
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
            {
                problems.Add($"Could not copy the database: {e.Message}");
            }
            finally
            {
                File.Delete(snapshot);
            }
        }

        var lastSuccess = destinations.Count > 0 ? started : BackupStatus.Read(new(home.Data))?.LastSuccess;
        WriteStatus(new BackupStatus(started, lastSuccess, destinations, problems));
        log.Write(destinations.Count > 0
            ? $"Done: {destinations.Count} folder(s), {problems.Count} problem(s)"
            : "FAILED: no backup written");
        return destinations.Count > 0 ? 0 : 1;
    }

    /// <summary>The folders in backup.conf: one per line, # starts a comment.</summary>
    public List<string> Folders()
    {
        if (!File.Exists(home.BackupConfig))
        {
            return [];
        }

        return File.ReadAllLines(home.BackupConfig)
            .Select(line => (line.IndexOf('#') is var hash and >= 0 ? line[..hash] : line).Trim())
            .Where(line => line.Length > 0)
            .ToList();
    }

    /// <summary>A new backup.conf, pointing at the home's own backups folder.</summary>
    public static void WriteDefaultConfig(ApertureHome home)
    {
        if (File.Exists(home.BackupConfig))
        {
            return;
        }

        File.WriteAllLines(home.BackupConfig,
        [
            "# Aperture backup folders, one per line. Folders that aren't there (e.g. a drive that isn't connected) are skipped.",
            "# Add an external drive or a synced folder for a copy off this computer.",
            home.Backups,
        ]);
    }

    private void BackUpTo(string folder, string snapshot, DateOnly today, List<string> destinations, List<string> problems)
    {
        // The home's own backups folder is Aperture's to create; any other missing folder is a drive that isn't there.
        if (Path.GetFullPath(folder) == Path.GetFullPath(home.Backups))
        {
            Directory.CreateDirectory(folder);
        }

        if (!Directory.Exists(folder))
        {
            problems.Add($"Not connected: {folder}");
            log.Write($"Skipped (not connected): {folder}");
            return;
        }

        var target = Path.Combine(folder, FolderName);
        try
        {
            var databases = Path.Combine(target, "database");
            Directory.CreateDirectory(databases);
            File.Copy(snapshot, Path.Combine(databases, $"aperture-{today:yyyy-MM-dd}.db"), overwrite: true);
            CopyNew(Path.Combine(home.Data, "attachments"), Path.Combine(target, "attachments"), replaceChanged: false);
            CopyNew(Path.Combine(home.Data, "keys"), Path.Combine(target, "keys"), replaceChanged: true);
            RemoveOldCopies(databases, today);
            destinations.Add(target);
            log.Write($"Backed up to {target}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problems.Add($"Backup failed: {target}");
            log.Write($"FAILED: {target}: {e.Message}");
        }
    }

    /// <summary>
    /// Copies files that aren't there yet. Attachments never change once stored; keys can, so they're replaced when
    /// they differ. Nothing is deleted: a file removed in Aperture by mistake is still in the backup.
    /// </summary>
    private static void CopyNew(string source, string target, bool replaceChanged)
    {
        if (!Directory.Exists(source))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(target, Path.GetRelativePath(source, file));
            var existing = new FileInfo(copy);
            if (existing.Exists && (!replaceChanged || existing.Length == new FileInfo(file).Length
                    && existing.LastWriteTimeUtc >= File.GetLastWriteTimeUtc(file)))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy, overwrite: true);
        }
    }

    /// <summary>Daily copies older than <see cref="KeepDays"/>, except those from the 1st of a month (kept forever).</summary>
    private static void RemoveOldCopies(string databases, DateOnly today)
    {
        foreach (var file in Directory.EnumerateFiles(databases, "aperture-*.db"))
        {
            var name = Path.GetFileNameWithoutExtension(file)["aperture-".Length..];
            if (DateOnly.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                && date.Day != 1 && date < today.AddDays(-KeepDays))
            {
                File.Delete(file);
            }
        }
    }

    private void WriteStatus(BackupStatus status)
    {
        Directory.CreateDirectory(home.Data);
        var temp = StatusFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(status));
        File.Move(temp, StatusFile, overwrite: true);
    }
}
