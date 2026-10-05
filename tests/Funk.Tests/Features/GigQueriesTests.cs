using Funk.Web.Data.Entities;
using Funk.Web.Features.Gigs;
using Microsoft.EntityFrameworkCore;

namespace Funk.Tests.Features;

public sealed class GigQueriesTests : IAsyncLifetime
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
        var wedding = await _database.AddGigAsync(client, At(12, 12, 20), At(12, 13, 2), title: "Wedding");

        // Late club set starting at 01:00 the next day
        var overlaps = await OverlapsAsync(At(12, 13, 1), At(12, 13, 5));

        Assert.Equal([wedding.Id], overlaps);
    }

    [Fact]
    public async Task Two_gigs_on_one_day_without_overlap_are_fine()
    {
        var client = await _database.AddClientAsync();
        await _database.AddGigAsync(client, At(12, 12, 13), At(12, 12, 16), title: "Ceremony");

        Assert.Empty(await OverlapsAsync(At(12, 12, 20), At(12, 13, 2)));
    }

    [Fact]
    public async Task Back_to_back_gigs_do_not_overlap()
    {
        var client = await _database.AddClientAsync();
        await _database.AddGigAsync(client, At(12, 12, 14), At(12, 12, 18));

        Assert.Empty(await OverlapsAsync(At(12, 12, 18), At(12, 12, 23)));
    }

    [Fact]
    public async Task Ignores_cancelled_gigs_and_the_gig_itself()
    {
        var client = await _database.AddClientAsync();
        await _database.AddGigAsync(client, At(12, 12, 20), At(12, 13, 2), GigStatus.Cancelled);
        var self = await _database.AddGigAsync(client, At(12, 12, 20), At(12, 13, 2));

        Assert.Empty(await OverlapsAsync(At(12, 12, 20), At(12, 13, 2), excludeGigId: self.Id));
    }

    [Fact]
    public async Task Gig_inside_another_overlaps()
    {
        var client = await _database.AddClientAsync();
        var festival = await _database.AddGigAsync(client, At(12, 12, 12), At(12, 13, 4), GigStatus.Inquiry);

        Assert.Equal([festival.Id], await OverlapsAsync(At(12, 12, 15), At(12, 12, 17)));
    }

    private async Task<List<int>> OverlapsAsync(DateTime start, DateTime end, int? excludeGigId = null)
    {
        await using var db = _database.CreateContext();
        return await db.Gigs.Overlapping(start, end, excludeGigId).Select(g => g.Id).ToListAsync();
    }

    // ---- Filter ----

    [Fact]
    public async Task Upcoming_includes_a_gig_in_progress_and_sorts_soonest_first()
    {
        var client = await _database.AddClientAsync();
        var later = await _database.AddGigAsync(client, At(12, 1, 20), At(12, 2, 2));
        var running = await _database.AddGigAsync(client, At(10, 6, 10), At(10, 6, 14));
        var soon = await _database.AddGigAsync(client, At(10, 10, 20), At(10, 11, 2));
        await _database.AddGigAsync(client, At(9, 1, 20), At(9, 2, 2));

        Assert.Equal([running.Id, soon.Id, later.Id], await FilterAsync(GigPeriod.Upcoming));
    }

    [Fact]
    public async Task Past_sorts_newest_first()
    {
        var client = await _database.AddClientAsync();
        var older = await _database.AddGigAsync(client, At(8, 1, 20), At(8, 2, 2));
        var newer = await _database.AddGigAsync(client, At(9, 1, 20), At(9, 2, 2));
        await _database.AddGigAsync(client, At(12, 1, 20), At(12, 2, 2));

        Assert.Equal([newer.Id, older.Id], await FilterAsync(GigPeriod.Past));
    }

    [Fact]
    public async Task Filters_by_status()
    {
        var client = await _database.AddClientAsync();
        var inquiry = await _database.AddGigAsync(client, At(11, 1, 20), At(11, 2, 2), GigStatus.Inquiry);
        await _database.AddGigAsync(client, At(11, 8, 20), At(11, 9, 2), GigStatus.Cancelled);

        Assert.Equal([inquiry.Id], await FilterAsync(GigPeriod.All, [GigStatus.Inquiry, GigStatus.Confirmed]));
    }

    [Theory]
    [InlineData("rossi")]        // client name, case-insensitive
    [InlineData("Hochzeit")]     // title
    [InlineData("laufen")]       // venue name / city
    [InlineData("Event AG")]     // client company
    public async Task Search_matches_title_client_and_venue(string search)
    {
        var rossi = await _database.AddClientAsync("Anna & Marco Rossi", company: "Event AG");
        var other = await _database.AddClientAsync("Someone Else");
        var venue = await _database.AddVenueAsync("Schloss Laufen", "Laufen-Uhwiesen");
        var match = await _database.AddGigAsync(rossi, At(11, 1, 20), At(11, 2, 2), venue: venue, title: "Hochzeit Rossi");
        await _database.AddGigAsync(other, At(11, 8, 20), At(11, 9, 2), title: "Birthday");

        Assert.Equal([match.Id], await FilterAsync(GigPeriod.All, search: search));
    }

    private async Task<List<int>> FilterAsync(GigPeriod period, GigStatus[]? statuses = null, string? search = null)
    {
        await using var db = _database.CreateContext();
        return await db.Gigs.Filter(period, statuses ?? [], search, Now).Select(g => g.Id).ToListAsync();
    }
}
