using System.IO.Compression;
using Aperture.Web.Configuration;
using Aperture.Web.Data;
using Aperture.Web.Data.Entities;
using Aperture.Web.Features.Backup;
using Microsoft.EntityFrameworkCore;

namespace Aperture.Tests.Features;

public sealed class BackupDownloadTests : IDisposable
{
    private readonly string _dataDirectory = Directory.CreateTempSubdirectory("aperture-backup-").FullName;

    public void Dispose() => Directory.Delete(_dataDirectory, recursive: true);

    [Fact]
    public async Task Zip_has_the_database_the_files_and_how_to_restore_but_not_the_login()
    {
        var paths = new AperturePaths(_dataDirectory);
        var options = new DbContextOptionsBuilder<ApertureDbContext>().UseSqlite(paths.ConnectionString).Options;
        await using (var db = new ApertureDbContext(options))
        {
            await db.Database.MigrateAsync();
            db.Clients.Add(new Client { Name = "Anna & Marco Rossi" });
            await db.SaveChangesAsync();
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.CreateDirectory(Path.Combine(paths.AttachmentsDirectory, "2026"));
        await File.WriteAllTextAsync(Path.Combine(paths.AttachmentsDirectory, "2026", "receipt.pdf"), "a receipt");
        Directory.CreateDirectory(paths.KeysDirectory);
        await File.WriteAllTextAsync(paths.LoginFile, "{}");

        var backup = new BackupDownload(paths, new FixedTimeProvider(new DateTime(2026, 10, 22, 18, 0, 0)));
        using var zipStream = new MemoryStream();
        await backup.WriteZipAsync(zipStream);

        zipStream.Position = 0;
        using var zip = new ZipArchive(zipStream);
        const string folder = "Aperture backup 2026-10-22";
        Assert.Equal("Aperture backup 2026-10-22.zip", backup.FileName);
        Assert.Equal(
            [$"{folder}/aperture.db", $"{folder}/attachments/2026/receipt.pdf", $"{folder}/How to restore.txt"],
            zip.Entries.Select(e => e.FullName));
        Assert.DoesNotContain(zip.Entries, e => e.FullName.Contains("keys") || e.FullName.Contains("login"));

        // The database copy opens and holds the data.
        var copy = Path.Combine(_dataDirectory, "restored.db");
        zip.GetEntry($"{folder}/aperture.db")!.ExtractToFile(copy);
        var restored = new DbContextOptionsBuilder<ApertureDbContext>().UseSqlite($"Data Source={copy};Pooling=False").Options;
        await using (var db = new ApertureDbContext(restored))
        {
            Assert.Equal("Anna & Marco Rossi", (await db.Clients.SingleAsync()).Name);
        }

        using var reader = new StreamReader(zip.GetEntry($"{folder}/How to restore.txt")!.Open());
        var note = await reader.ReadToEndAsync();
        Assert.Contains("Aperture.Web restore ~/Downloads/\"Aperture backup 2026-10-22.zip\"", note);   // Mac and Linux
        Assert.Contains(@"Aperture.Web.exe"" restore ""$env:USERPROFILE\Downloads\Aperture backup 2026-10-22.zip""", note); // Windows
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }
}
