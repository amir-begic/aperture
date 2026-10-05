using Funk.Web.Data;
using Funk.Web.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Funk.Tests;

/// <summary>
/// An in-memory SQLite database with the real migrations applied. Lives as long as the
/// connection, so create one per test (xUnit creates a new test class instance per test).
/// </summary>
public sealed class TestDatabase : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _connection.DisposeAsync().AsTask();

    public FunkDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FunkDbContext>().UseSqlite(_connection).Options);

    /// <summary>Executes raw SQL and returns the first column of the first row.</summary>
    public async Task<object?> ScalarAsync(string sql)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    public async Task<Client> AddClientAsync(string name = "Anna & Marco Rossi", string? company = null)
    {
        await using var db = CreateContext();
        var client = new Client { Name = name, Company = company };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return client;
    }

    public async Task<Venue> AddVenueAsync(string name = "Schloss Laufen", string? city = "Laufen-Uhwiesen")
    {
        await using var db = CreateContext();
        var venue = new Venue { Name = name, City = city };
        db.Venues.Add(venue);
        await db.SaveChangesAsync();
        return venue;
    }

    public async Task<Gig> AddGigAsync(
        Client client, DateTime start, DateTime end,
        GigStatus status = GigStatus.Confirmed, Venue? venue = null, string title = "Wedding")
    {
        await using var db = CreateContext();
        var gig = new Gig
        {
            Title = title,
            ClientId = client.Id,
            VenueId = venue?.Id,
            Start = start,
            End = end,
            Status = status,
        };
        db.Gigs.Add(gig);
        await db.SaveChangesAsync();
        return gig;
    }
}
