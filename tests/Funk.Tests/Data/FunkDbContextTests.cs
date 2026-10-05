using Funk.Web.Data.Entities;
using Funk.Web.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Funk.Tests.Data;

/// <summary>Runs the real migrations against an in-memory SQLite database.</summary>
public sealed class FunkDbContextTests : IAsyncLifetime
{
    private readonly TestDatabase _database = new();

    public Task InitializeAsync() => _database.InitializeAsync();

    public Task DisposeAsync() => _database.DisposeAsync();

    [Fact]
    public async Task Migrations_seed_one_incomplete_settings_row()
    {
        await using var db = _database.CreateContext();

        var settings = await db.BusinessSettings.SingleAsync();

        Assert.Equal(BusinessSettings.SingletonId, settings.Id);
        Assert.Equal("CH", settings.CountryCode);
        Assert.Equal(8.1m, settings.VatRatePercent);
        Assert.False(settings.IsComplete);
    }

    [Fact]
    public async Task Settings_round_trip()
    {
        await using (var db = _database.CreateContext())
        {
            var settings = await db.BusinessSettings.SingleAsync();
            settings.OwnerName = "Max Muster";
            settings.Street = "Musterstrasse 1";
            settings.PostalCode = "8000";
            settings.City = "Zürich";
            settings.Iban = "CH9300762011623852957";
            await db.SaveChangesAsync();
        }

        await using (var db = _database.CreateContext())
        {
            var settings = await db.BusinessSettings.SingleAsync();
            Assert.Equal("Zürich", settings.City);
            Assert.True(settings.IsComplete);
        }
    }

    [Fact]
    public async Task Quoted_price_is_stored_as_rappen_and_may_be_empty()
    {
        var client = await _database.AddClientAsync();
        var priced = await _database.AddGigAsync(client, new DateTime(2026, 12, 12, 20, 0, 0), new DateTime(2026, 12, 13, 2, 0, 0));
        var unpriced = await _database.AddGigAsync(client, new DateTime(2026, 12, 19, 20, 0, 0), new DateTime(2026, 12, 20, 2, 0, 0));

        await using (var db = _database.CreateContext())
        {
            (await db.Gigs.SingleAsync(g => g.Id == priced.Id)).QuotedPrice = Money.FromChf(1850.50m);
            await db.SaveChangesAsync();
        }

        Assert.Equal(185050L, await _database.ScalarAsync($"SELECT QuotedPrice FROM Gigs WHERE Id = {priced.Id}"));
        await using (var db = _database.CreateContext())
        {
            Assert.Equal(Money.FromChf(1850.50m), (await db.Gigs.SingleAsync(g => g.Id == priced.Id)).QuotedPrice);
            Assert.Null((await db.Gigs.SingleAsync(g => g.Id == unpriced.Id)).QuotedPrice);
        }
    }

    [Fact]
    public async Task Enums_are_stored_as_text()
    {
        var client = await _database.AddClientAsync();
        var gig = await _database.AddGigAsync(client, new DateTime(2026, 12, 12, 20, 0, 0), new DateTime(2026, 12, 13, 2, 0, 0), GigStatus.Inquiry);

        Assert.Equal("Inquiry", await _database.ScalarAsync($"SELECT Status FROM Gigs WHERE Id = {gig.Id}"));
    }

    [Fact]
    public async Task Clients_and_venues_with_gigs_cannot_be_deleted()
    {
        var client = await _database.AddClientAsync();
        var venue = await _database.AddVenueAsync();
        await _database.AddGigAsync(client, new DateTime(2026, 12, 12, 20, 0, 0), new DateTime(2026, 12, 13, 2, 0, 0), venue: venue);

        await using var db = _database.CreateContext();
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => db.Clients.Where(c => c.Id == client.Id).ExecuteDeleteAsync());
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => db.Venues.Where(v => v.Id == venue.Id).ExecuteDeleteAsync());
    }

    [Fact]
    public async Task Blank_optional_text_is_stored_as_null()
    {
        await using (var db = _database.CreateContext())
        {
            db.Clients.Add(new Client { Name = "Anna", Phone = "+41 79 123 45 67", Email = "", Company = "  " });
            await db.SaveChangesAsync();
        }

        await using (var db = _database.CreateContext())
        {
            var client = await db.Clients.SingleAsync();
            Assert.Equal("+41 79 123 45 67", client.Phone);
            Assert.Null(client.Email);
            Assert.Null(client.Company);
            Assert.Equal("Anna", client.Name); // required text is left alone
        }
    }

    [Fact]
    public async Task Upgrading_from_the_first_migration_keeps_existing_settings()
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new Funk.Web.Data.FunkDbContext(
            new DbContextOptionsBuilder<Funk.Web.Data.FunkDbContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();

        var migrations = db.Database.GetMigrations().ToList();
        await migrator.MigrateAsync(migrations[0]);
        await db.Database.ExecuteSqlRawAsync("UPDATE BusinessSettings SET OwnerName = 'Max Muster' WHERE Id = 1");

        await migrator.MigrateAsync(); // all remaining migrations

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal("Max Muster", (await db.BusinessSettings.SingleAsync()).OwnerName);
    }

    [Fact]
    public async Task Model_has_no_pending_changes()
    {
        await using var db = _database.CreateContext();

        Assert.False(db.Database.HasPendingModelChanges(),
            "The model changed without a migration. Run: dotnet ef migrations add <Name> --project src/Funk.Web --output-dir Data/Migrations");
    }
}
