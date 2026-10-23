using Aperture.Web.Configuration;
using Aperture.Web.Features.Backup;
using Aperture.Web.Features.Dashboard;

namespace Aperture.Tests.Features;

public sealed class BackupStatusTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 6, 9, 0, 0);
    private readonly string _dataDirectory = Directory.CreateTempSubdirectory("aperture-tests-").FullName;
    private readonly TestDatabase _database = new();
    private AperturePaths Paths => new(_dataDirectory);

    public Task InitializeAsync() => _database.InitializeAsync();

    public async Task DisposeAsync()
    {
        await _database.DisposeAsync();
        Directory.Delete(_dataDirectory, recursive: true);
    }

    // Exactly the format "Aperture.Web backup" (BackupJob) writes.
    private void WriteStatus(string lastSuccess, params string[] problems) => File.WriteAllText(Paths.BackupStatusFile, $$"""
        {"lastRun":"2026-10-06T01:30:12Z","lastSuccess":{{lastSuccess}},"destinations":["/Volumes/Backup A/Aperture"],"problems":[{{string.Join(",", problems.Select(p => $"\"{p}\""))}}]}
        """);

    private async Task<List<AttentionItem>> ItemsAsync(bool expectBackups = true)
    {
        await using var db = _database.CreateContext();
        return await Attention.ItemsAsync(db, Now, BackupStatus.Read(Paths), expectBackups);
    }

    [Fact]
    public void Reads_the_script_output()
    {
        WriteStatus("\"2026-10-06T01:30:40Z\"", "Backup failed: /Users/anna/Aperture/backups/Aperture Backup");

        var status = BackupStatus.Read(Paths)!;

        Assert.Equal(new DateTimeOffset(2026, 10, 6, 1, 30, 40, TimeSpan.Zero), status.LastSuccess);
        Assert.Equal(["/Volumes/Backup A/Aperture"], status.Destinations);
        Assert.Equal(["Backup failed: /Users/anna/Aperture/backups/Aperture Backup"], status.Problems);
    }

    [Fact]
    public void Missing_or_broken_file_means_no_status()
    {
        Assert.Null(BackupStatus.Read(Paths));
        File.WriteAllText(Paths.BackupStatusFile, "{ not json");
        Assert.Null(BackupStatus.Read(Paths));
    }

    [Fact]
    public async Task Never_backed_up_is_urgent_where_backups_are_expected()
    {
        Assert.Equal("No backup has run yet", Assert.Single(await ItemsAsync()).Text);
        Assert.Empty(await ItemsAsync(expectBackups: false)); // development machine
    }

    [Fact]
    public async Task Fresh_backup_needs_nothing()
    {
        WriteStatus("\"2026-10-06T01:30:40Z\"");

        Assert.Empty(await ItemsAsync());
    }

    [Fact]
    public async Task Stale_backup_and_disconnected_drive_are_reported()
    {
        WriteStatus("\"2026-10-03T01:30:40Z\"", "Backup failed: /Users/anna/Aperture/backups/Aperture Backup");

        var items = await ItemsAsync();

        Assert.Equal(AttentionLevel.Urgent, items[0].Level);
        Assert.StartsWith("Last backup: 03.10.2026", items[0].Text);
        Assert.Equal(("Backup problem", "Backup failed: /Users/anna/Aperture/backups/Aperture Backup"), (items[1].Text, items[1].Detail));
    }

    [Fact]
    public async Task Failed_runs_without_any_success_count_as_never()
    {
        WriteStatus("null", "No backup drive connected");

        Assert.Equal(["No backup has run yet", "Backup problem"], (await ItemsAsync()).Select(i => i.Text));
    }
}
