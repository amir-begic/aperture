using Aperture.Web.Data.Entities;
using Aperture.Web.Domain;
using Aperture.Web.Features.Shoots.Finances;

namespace Aperture.Tests.Features;

public sealed class TrainTripsTests : IAsyncLifetime
{
    private static readonly DateTime Day = new(2026, 10, 10, 13, 0, 0);
    private readonly TestDatabase _db = new();

    public Task InitializeAsync() => _db.InitializeAsync();

    public Task DisposeAsync() => _db.DisposeAsync();

    private async Task AddCostAsync(Shoot shoot, DateOnly date, ExpenseCategory category, string? vendor, decimal chf)
    {
        await using var db = _db.CreateContext();
        db.Expenses.Add(new Expense
        {
            ShootId = shoot.Id, Date = date, Category = category, Vendor = vendor, Description = "Ticket", Amount = Money.FromChf(chf),
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Suggests_the_last_fare_to_the_same_location()
    {
        var client = await _db.AddClientAsync();
        var laufen = await _db.AddLocationAsync();
        var bern = await _db.AddLocationAsync("Rosengarten", "Bern");
        var earlier = await _db.AddShootAsync(client, Day.AddMonths(-3), Day.AddMonths(-3).AddHours(5), location: laufen);
        var last = await _db.AddShootAsync(client, Day.AddMonths(-1), Day.AddMonths(-1).AddHours(5), location: laufen);
        var elsewhere = await _db.AddShootAsync(client, Day.AddDays(-5), Day.AddDays(-5).AddHours(5), location: bern);
        var next = await _db.AddShootAsync(client, Day, Day.AddHours(5), location: laufen);
        await AddCostAsync(earlier, new DateOnly(2026, 7, 10), ExpenseCategory.Travel, TrainTrips.Vendor, 31.20m);
        await AddCostAsync(last, new DateOnly(2026, 9, 10), ExpenseCategory.Travel, TrainTrips.Vendor, 33.60m);
        await AddCostAsync(last, new DateOnly(2026, 9, 11), ExpenseCategory.Travel, "Taxi Meier", 45m);
        await AddCostAsync(elsewhere, new DateOnly(2026, 10, 5), ExpenseCategory.Travel, TrainTrips.Vendor, 52m);

        await using var db = _db.CreateContext();
        var preset = await TrainTrips.SuggestAsync(db, next.Id);

        Assert.Equal(ExpenseCategory.Travel, preset.Category);
        Assert.Equal("Train to Laufen-Uhwiesen and back", preset.Description);
        Assert.Equal("SBB", preset.Vendor);
        Assert.Equal(Money.FromChf(33.60m), preset.Amount);
    }

    [Fact]
    public async Task Without_a_location_there_is_no_fare_to_suggest()
    {
        var client = await _db.AddClientAsync();
        var shoot = await _db.AddShootAsync(client, Day, Day.AddHours(5));

        await using var db = _db.CreateContext();
        var preset = await TrainTrips.SuggestAsync(db, shoot.Id);

        Assert.Equal("Train to the shoot and back", preset.Description);
        Assert.Null(preset.Amount);
    }
}
