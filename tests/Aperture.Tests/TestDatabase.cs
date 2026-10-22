using Aperture.Web.Data;
using Aperture.Web.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Aperture.Tests;

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

    public ApertureDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApertureDbContext>().UseSqlite(_connection).Options);

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

    public async Task<Location> AddLocationAsync(string name = "Schloss Laufen", string? city = "Laufen-Uhwiesen")
    {
        await using var db = CreateContext();
        var location = new Location { Name = name, City = city };
        db.Locations.Add(location);
        await db.SaveChangesAsync();
        return location;
    }

    public async Task<Shoot> AddShootAsync(
        Client client, DateTime start, DateTime end,
        ShootStatus status = ShootStatus.Booked, Location? location = null, string title = "Wedding")
    {
        await using var db = CreateContext();
        var shoot = new Shoot
        {
            Title = title,
            ClientId = client.Id,
            LocationId = location?.Id,
            Start = start,
            End = end,
            Status = status,
        };
        db.Shoots.Add(shoot);
        await db.SaveChangesAsync();
        return shoot;
    }

    public async Task<Payment> AddPaymentAsync(Shoot shoot, decimal chf)
    {
        await using var db = CreateContext();
        var payment = new Payment { ShootId = shoot.Id, Amount = Aperture.Web.Domain.Money.FromChf(chf), Date = new DateOnly(2026, 12, 14) };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        return payment;
    }
}
