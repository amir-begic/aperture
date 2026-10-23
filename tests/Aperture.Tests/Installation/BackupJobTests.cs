using Aperture.Web.Configuration;
using Aperture.Web.Data;
using Aperture.Web.Data.Entities;
using Aperture.Web.Features.Backup;
using Aperture.Web.Installation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Aperture.Tests.Installation;

public sealed class BackupJobTests : IAsyncLifetime
{
    private static readonly DateTime Today = new(2026, 10, 23, 13, 0, 0);
    private readonly string _root = Directory.CreateTempSubdirectory("aperture-home-").FullName;
    private ApertureHome Home => new(_root);

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<ApertureDbContext>()
            .UseSqlite($"Data Source={Path.Combine(Home.Data, "aperture.db")};Pooling=False").Options;
        Directory.CreateDirectory(Home.Data);
        await using var db = new ApertureDbContext(options);
        await db.Database.MigrateAsync();
        db.Clients.Add(new Client { Name = "Anna & Marco Rossi" });
        await db.SaveChangesAsync();

        Directory.CreateDirectory(Path.Combine(Home.Data, "attachments", "2026"));
        await File.WriteAllTextAsync(Path.Combine(Home.Data, "attachments", "2026", "receipt.pdf"), "a receipt");
        Directory.CreateDirectory(Path.Combine(Home.Data, "keys"));
        await File.WriteAllTextAsync(Path.Combine(Home.Data, "keys", "login.json"), "{}");
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
        return Task.CompletedTask;
    }

    private BackupJob Job(DateTime? now = null) =>
        new(Home, new FixedTimeProvider(now ?? Today), new CommandLog(null));

    private BackupStatus Status => BackupStatus.Read(new AperturePaths(Home.Data))!;

    [Fact]
    public void Backs_up_database_files_and_keys_into_the_install_folder()
    {

        Assert.Equal(0, Job().Run());

        var target = Job().Target;
        Assert.Equal(Path.Combine(Home.Backups, BackupJob.FolderName), target);
        var copy = Path.Combine(target, "database", "aperture-2026-10-23.db");
        Assert.Equal("ok", DatabaseCopy.IntegrityCheck(copy));
        Assert.Equal("a receipt", File.ReadAllText(Path.Combine(target, "attachments", "2026", "receipt.pdf")));
        Assert.True(File.Exists(Path.Combine(target, "keys", "login.json")));
        Assert.Equal([target], Status.Destinations);
        Assert.Empty(Status.Problems);
        Assert.Equal(new FixedTimeProvider(Today).GetUtcNow(), Status.LastSuccess);
    }

    [Fact]
    public void Files_deleted_in_Aperture_stay_in_the_backup()
    {
        Job().Run();
        File.Delete(Path.Combine(Home.Data, "attachments", "2026", "receipt.pdf"));

        Job(Today.AddDays(1)).Run();

        Assert.True(File.Exists(Path.Combine(Home.Backups, BackupJob.FolderName, "attachments", "2026", "receipt.pdf")));
    }

    [Fact]
    public void Without_any_backup_the_last_success_stays_as_it_was()
    {
        Job().Run();
        File.Move(Path.Combine(Home.Data, "aperture.db"), Path.Combine(Home.Data, "moved.db"));

        Assert.Equal(1, Job(Today.AddDays(1)).Run());
        Assert.Equal([$"No database found in {Home.Data}"], Status.Problems);

        Assert.Equal(new FixedTimeProvider(Today).GetUtcNow(), Status.LastSuccess);
        Assert.Equal(new FixedTimeProvider(Today.AddDays(1)).GetUtcNow(), Status.LastRun);
    }

    [Fact]
    public void Old_daily_copies_go_except_those_from_the_first_of_a_month()
    {
        var databases = Path.Combine(Home.Backups, BackupJob.FolderName, "database");
        Directory.CreateDirectory(databases);
        foreach (var day in new[] { "2026-07-01", "2026-07-02", "2026-07-25", "2026-07-26" })
        {
            File.WriteAllText(Path.Combine(databases, $"aperture-{day}.db"), "");
        }

        Job().Run();

        // 90 days before 23.10. is 25.07.: older daily copies go, the 1st of the month stays.
        Assert.Equal(["aperture-2026-07-01.db", "aperture-2026-07-25.db", "aperture-2026-07-26.db", "aperture-2026-10-23.db"],
            Directory.GetFiles(databases).Select(Path.GetFileName).Order());
    }
}
