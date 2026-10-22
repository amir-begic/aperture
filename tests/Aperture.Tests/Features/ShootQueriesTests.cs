using Aperture.Web.Data.Entities;
using Aperture.Web.Features.Shoots;
using Microsoft.EntityFrameworkCore;

namespace Aperture.Tests.Features;

public sealed class ShootQueriesTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0);
    private readonly TestDatabase _database = new();

    public Task InitializeAsync() => _database.InitializeAsync();

    public Task DisposeAsync() => _database.DisposeAsync();

    private static DateTime At(int month, int day, int hour) => new(2026, month, day, hour, 0, 0);

    // ---- Overlapping ----

    [Fact]
    public async Task Detects_overlap_across_midnight()
    {
        var client = await _database.AddClientAsync();
        var wedding = await _database.AddShootAsync(client, At(12, 12, 20), At(12, 13, 2), title: "Wedding");

        // Late reception coverage starting at 01:00 the next day
        var overlaps = await OverlapsAsync(At(12, 13, 1), At(12, 13, 5));

        Assert.Equal([wedding.Id], overlaps);
    }

    [Fact]
    public async Task Two_shoots_on_one_day_without_overlap_are_fine()
    {
        var client = await _database.AddClientAsync();
        await _database.AddShootAsync(client, At(12, 12, 13), At(12, 12, 16), title: "Ceremony");

        Assert.Empty(await OverlapsAsync(At(12, 12, 20), At(12, 13, 2)));
    }

    [Fact]
    public async Task Back_to_back_shoots_do_not_overlap()
    {
        var client = await _database.AddClientAsync();
        await _database.AddShootAsync(client, At(12, 12, 14), At(12, 12, 18));

        Assert.Empty(await OverlapsAsync(At(12, 12, 18), At(12, 12, 23)));
    }

    [Fact]
    public async Task Ignores_cancelled_shoots_and_the_shoot_itself()
    {
        var client = await _database.AddClientAsync();
        await _database.AddShootAsync(client, At(12, 12, 20), At(12, 13, 2), ShootStatus.Cancelled);
        var self = await _database.AddShootAsync(client, At(12, 12, 20), At(12, 13, 2));

        Assert.Empty(await OverlapsAsync(At(12, 12, 20), At(12, 13, 2), excludeShootId: self.Id));
    }

    [Fact]
    public async Task Shoot_inside_another_overlaps()
    {
        var client = await _database.AddClientAsync();
        var festival = await _database.AddShootAsync(client, At(12, 12, 12), At(12, 13, 4), ShootStatus.Inquiry);

        Assert.Equal([festival.Id], await OverlapsAsync(At(12, 12, 15), At(12, 12, 17)));
    }

    private async Task<List<int>> OverlapsAsync(DateTime start, DateTime end, int? excludeShootId = null)
    {
        await using var db = _database.CreateContext();
        return await db.Shoots.Overlapping(start, end, excludeShootId).Select(s => s.Id).ToListAsync();
    }

    // ---- InRange (calendar) ----

    [Fact]
    public async Task Range_includes_shoots_that_start_before_or_end_after_it()
    {
        var client = await _database.AddClientAsync();
        // Visible month: December 2026
        var newYearsEve = await _database.AddShootAsync(client, new DateTime(2026, 12, 31, 21, 0, 0), new DateTime(2027, 1, 1, 4, 0, 0));
        var lateNovember = await _database.AddShootAsync(client, At(11, 30, 22), At(12, 1, 3));
        await _database.AddShootAsync(client, At(11, 28, 20), At(11, 29, 2));
        await _database.AddShootAsync(client, new DateTime(2027, 1, 1, 20, 0, 0), new DateTime(2027, 1, 2, 2, 0, 0));

        await using var db = _database.CreateContext();
        var ids = await db.Shoots.InRange(At(12, 1, 0), new DateTime(2027, 1, 1), includeCancelled: false)
            .OrderBy(s => s.Start).Select(s => s.Id).ToListAsync();

        Assert.Equal([lateNovember.Id, newYearsEve.Id], ids);
    }

    [Fact]
    public async Task Range_shows_cancelled_shoots_only_on_request()
    {
        var client = await _database.AddClientAsync();
        var cancelled = await _database.AddShootAsync(client, At(12, 12, 20), At(12, 13, 2), ShootStatus.Cancelled);

        await using var db = _database.CreateContext();
        Assert.Empty(await db.Shoots.InRange(At(12, 1, 0), At(12, 31, 0), includeCancelled: false).ToListAsync());
        Assert.Equal(cancelled.Id, (await db.Shoots.InRange(At(12, 1, 0), At(12, 31, 0), includeCancelled: true).SingleAsync()).Id);
    }

    // ---- Filter ----

    [Fact]
    public async Task Upcoming_includes_a_shoot_in_progress_and_sorts_soonest_first()
    {
        var client = await _database.AddClientAsync();
        var later = await _database.AddShootAsync(client, At(12, 1, 20), At(12, 2, 2));
        var running = await _database.AddShootAsync(client, At(10, 6, 10), At(10, 6, 14));
        var soon = await _database.AddShootAsync(client, At(10, 10, 20), At(10, 11, 2));
        await _database.AddShootAsync(client, At(9, 1, 20), At(9, 2, 2));

        Assert.Equal([running.Id, soon.Id, later.Id], await FilterAsync(ShootPeriod.Upcoming));
    }

    [Fact]
    public async Task Past_sorts_newest_first()
    {
        var client = await _database.AddClientAsync();
        var older = await _database.AddShootAsync(client, At(8, 1, 20), At(8, 2, 2));
        var newer = await _database.AddShootAsync(client, At(9, 1, 20), At(9, 2, 2));
        await _database.AddShootAsync(client, At(12, 1, 20), At(12, 2, 2));

        Assert.Equal([newer.Id, older.Id], await FilterAsync(ShootPeriod.Past));
    }

    [Fact]
    public async Task Filters_by_status()
    {
        var client = await _database.AddClientAsync();
        var inquiry = await _database.AddShootAsync(client, At(11, 1, 20), At(11, 2, 2), ShootStatus.Inquiry);
        await _database.AddShootAsync(client, At(11, 8, 20), At(11, 9, 2), ShootStatus.Cancelled);

        Assert.Equal([inquiry.Id], await FilterAsync(ShootPeriod.All, [ShootStatus.Inquiry, ShootStatus.Booked]));
    }

    [Theory]
    [InlineData("rossi")]        // client name, case-insensitive
    [InlineData("Hochzeit")]     // title
    [InlineData("laufen")]       // location name / city
    [InlineData("Event AG")]     // client company
    public async Task Search_matches_title_client_and_location(string search)
    {
        var rossi = await _database.AddClientAsync("Anna & Marco Rossi", company: "Event AG");
        var other = await _database.AddClientAsync("Someone Else");
        var location = await _database.AddLocationAsync("Schloss Laufen", "Laufen-Uhwiesen");
        var match = await _database.AddShootAsync(rossi, At(11, 1, 20), At(11, 2, 2), location: location, title: "Hochzeit Rossi");
        await _database.AddShootAsync(other, At(11, 8, 20), At(11, 9, 2), title: "Birthday");

        Assert.Equal([match.Id], await FilterAsync(ShootPeriod.All, search: search));
    }

    private async Task<List<int>> FilterAsync(ShootPeriod period, ShootStatus[]? statuses = null, string? search = null)
    {
        await using var db = _database.CreateContext();
        return await db.Shoots.Filter(period, statuses ?? [], search, Now).Select(s => s.Id).ToListAsync();
    }
}
