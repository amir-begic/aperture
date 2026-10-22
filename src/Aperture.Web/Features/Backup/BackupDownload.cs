using System.IO.Compression;
using System.Text;
using Aperture.Web.Configuration;
using Microsoft.Data.Sqlite;

namespace Aperture.Web.Features.Backup;

/// <summary>
/// Everything in one ZIP (Settings → Backups → Download a backup): a consistent copy of the database and all uploaded
/// files, with a note on how to restore it, e.g. on a new Mac. The login and the notification keys stay out: a new
/// installation creates its own login, and notifications are turned on again there.
/// </summary>
public sealed class BackupDownload(AperturePaths paths, TimeProvider time)
{
    public string FileName => $"{FolderName}.zip";

    private string FolderName => $"Aperture backup {time.GetLocalNow():yyyy-MM-dd}";

    /// <summary>The ZIP in a temporary file that's deleted when the stream is closed.</summary>
    public async Task<Stream> CreateZipAsync(CancellationToken cancellationToken = default)
    {
        var file = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None,
            bufferSize: 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            await WriteZipAsync(file, cancellationToken);
            file.Position = 0;
            return file;
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    public async Task WriteZipAsync(Stream output, CancellationToken cancellationToken = default)
    {
        var folder = FolderName;
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        // SQLite's online backup: a consistent copy, also while Aperture writes to the database.
        var copy = Path.GetTempFileName();
        try
        {
            using (var source = new SqliteConnection(paths.ConnectionString))
            using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = copy, Pooling = false }.ToString()))
            {
                source.Open();
                source.BackupDatabase(target);
            }
            await AddFileAsync(zip, $"{folder}/aperture.db", copy, cancellationToken);
        }
        finally
        {
            File.Delete(copy);
        }

        if (Directory.Exists(paths.AttachmentsDirectory))
        {
            foreach (var path in Directory.EnumerateFiles(paths.AttachmentsDirectory, "*", SearchOption.AllDirectories).Order())
            {
                var relative = Path.GetRelativePath(paths.AttachmentsDirectory, path).Replace(Path.DirectorySeparatorChar, '/');
                await AddFileAsync(zip, $"{folder}/attachments/{relative}", path, cancellationToken);
            }
        }

        var readme = zip.CreateEntry($"{folder}/How to restore.txt");
        await using var writer = new StreamWriter(readme.Open(), new UTF8Encoding(false));
        await writer.WriteAsync(RestoreNote(folder));
    }

    private static async Task AddFileAsync(ZipArchive zip, string name, string path, CancellationToken cancellationToken)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        await using var target = entry.Open();
        await using var source = File.OpenRead(path);
        await source.CopyToAsync(target, cancellationToken);
    }

    private static string RestoreNote(string folder) => $"""
        Aperture backup: the database (aperture.db) and all uploaded files (attachments).

        To restore it, e.g. on a new Mac:

        1. Install Aperture with its installer and create a login (any; the backup has none).
        2. Double-click this ZIP in Downloads, so the folder "{folder}" is next to it.
        3. In Terminal, run these lines one by one. They stop Aperture, put the backup in place and start it again.
           Your data in Aperture is replaced by the backup's.

           launchctl bootout gui/$UID/ch.aperture.web
           cd ~/Downloads/"{folder}"
           cp aperture.db ~/Aperture/data/aperture.db
           rm -f ~/Aperture/data/aperture.db-wal ~/Aperture/data/aperture.db-shm
           mkdir -p ~/Aperture/data/attachments && rsync -a attachments/ ~/Aperture/data/attachments/
           launchctl bootstrap gui/$UID ~/Library/LaunchAgents/ch.aperture.web.plist

        4. Open http://localhost:5090 and sign in. Turn notifications on again under Settings if you use them.

        """;
}

public static class BackupDownloadEndpoints
{
    /// <summary>GET /backup.zip: see <see cref="BackupDownload"/>. Requires sign-in like every other endpoint.</summary>
    public static void MapBackupDownloadEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/backup.zip", async (BackupDownload backup, CancellationToken cancellationToken) =>
            Results.File(await backup.CreateZipAsync(cancellationToken), "application/zip", backup.FileName)); // disposing deletes the temp file
    }
}
