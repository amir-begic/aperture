using Funk.Web.Data;
using Funk.Web.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Funk.Tests.Data;

/// <summary>Runs the real migrations against an in-memory SQLite database.</summary>
public sealed class FunkDbContextTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _connection.DisposeAsync().AsTask();

    private FunkDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FunkDbContext>().UseSqlite(_connection).Options);

    [Fact]
    public async Task Migrations_seed_one_incomplete_settings_row()
    {
        await using var db = CreateContext();

        var settings = await db.BusinessSettings.SingleAsync();

        Assert.Equal(BusinessSettings.SingletonId, settings.Id);
        Assert.Equal("CH", settings.CountryCode);
        Assert.Equal(8.1m, settings.VatRatePercent);
        Assert.False(settings.IsComplete);
    }

    [Fact]
    public async Task Settings_round_trip()
    {
        await using (var db = CreateContext())
        {
            var settings = await db.BusinessSettings.SingleAsync();
            settings.OwnerName = "Max Muster";
            settings.Street = "Musterstrasse 1";
            settings.PostalCode = "8000";
            settings.City = "Zürich";
            settings.Iban = "CH9300762011623852957";
            await db.SaveChangesAsync();
        }

        await using (var db = CreateContext())
        {
            var settings = await db.BusinessSettings.SingleAsync();
            Assert.Equal("Zürich", settings.City);
            Assert.True(settings.IsComplete);
        }
    }

    [Fact]
    public void Model_has_no_pending_changes()
    {
        using var db = CreateContext();

        Assert.False(db.Database.HasPendingModelChanges(),
            "The model changed without a migration. Run: dotnet ef migrations add <Name> --project src/Funk.Web --output-dir Data/Migrations");
    }
}
