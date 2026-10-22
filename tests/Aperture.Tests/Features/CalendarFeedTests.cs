using System.Text;
using Aperture.Web.Data.Entities;
using Aperture.Web.Features.CalendarFeed;
using Microsoft.EntityFrameworkCore;

namespace Aperture.Tests.Features;

public class CalendarFeedTests : IAsyncLifetime
{
    private readonly TestDatabase _db = new();

    public Task InitializeAsync() => _db.InitializeAsync();

    public Task DisposeAsync() => _db.DisposeAsync();

    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0);

    private static FeedEvent Event(
        ShootStatus status = ShootStatus.Confirmed, string title = "Wedding Rossi", string? notes = null,
        EventType type = EventType.Wedding, string? location = "Schloss Laufen, 8447 Laufen-Uhwiesen") =>
        new(7, title, status, type, new DateTime(2026, 12, 12, 20, 0, 0), new DateTime(2026, 12, 13, 2, 0, 0),
            "Anna & Marco Rossi", "+41 79 123 45 67", location, notes);

    private static string Write(params FeedEvent[] events) =>
        IcsCalendar.Write(events, "https://mini.example.ts.net/", new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc));

    /// <summary>Undoes line folding, so assertions can look at whole properties.</summary>
    private static string[] Unfolded(string ics) => ics.Replace("\r\n ", "").Split("\r\n");

    [Fact]
    public void Writes_a_calendar_with_the_zurich_time_zone_and_crlf_lines()
    {
        var ics = Write(Event());

        Assert.StartsWith("BEGIN:VCALENDAR\r\nVERSION:2.0\r\n", ics);
        Assert.EndsWith("END:VCALENDAR\r\n", ics);
        Assert.DoesNotContain("\n", ics.Replace("\r\n", ""));
        Assert.Contains("TZID:Europe/Zurich", ics);
        Assert.Contains("RRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=-1SU", ics);
    }

    [Fact]
    public void Event_has_local_times_a_stable_uid_and_a_link_back()
    {
        var lines = Unfolded(Write(Event()));

        Assert.Contains("UID:shoot-7@aperture", lines);
        Assert.Contains("DTSTAMP:20261020T100000Z", lines);
        Assert.Contains("DTSTART;TZID=Europe/Zurich:20261212T200000", lines);
        Assert.Contains("DTEND;TZID=Europe/Zurich:20261213T020000", lines);
        Assert.Contains("URL:https://mini.example.ts.net/shoots/7", lines);
        Assert.Contains("STATUS:CONFIRMED", lines);
        Assert.Contains("SUMMARY:Wedding Rossi", lines);
        Assert.Contains("LOCATION:Schloss Laufen\\, 8447 Laufen-Uhwiesen", lines);
    }

    [Fact]
    public void Description_has_status_client_phone_and_notes()
    {
        var description = Unfolded(Write(Event(notes: "Ceremony 14:00; first dance: \"Perfect\"")))
            .Single(l => l.StartsWith("DESCRIPTION:"));

        Assert.Equal(
            "DESCRIPTION:Confirmed · Wedding\\nAnna & Marco Rossi · +41 79 123 45 67\\n\\nCeremony 14:00\\; first dance: \"Perfect\"\\n\\nhttps://mini.example.ts.net/shoots/7",
            description);
    }

    [Fact]
    public void Inquiries_are_tentative_and_marked_in_the_title()
    {
        var lines = Unfolded(Write(Event(ShootStatus.Inquiry, type: EventType.Other)));

        Assert.Contains("STATUS:TENTATIVE", lines);
        Assert.Contains("SUMMARY:Wedding Rossi (inquiry)", lines);
        Assert.Contains(lines, l => l.StartsWith("DESCRIPTION:Inquiry\\n"));
    }

    [Fact]
    public void Shoot_without_location_leaves_LOCATION_out()
    {
        Assert.DoesNotContain("LOCATION:", Write(Event(location: null)));
    }

    [Theory]
    [InlineData("a,b;c\\d", "a\\,b\\;c\\\\d")]
    [InlineData("line 1\r\nline 2\nline 3", "line 1\\nline 2\\nline 3")]
    public void Escapes_text_values(string text, string expected) => Assert.Equal(expected, IcsCalendar.Escape(text));

    [Fact]
    public void Folds_long_lines_at_75_octets_without_splitting_characters()
    {
        var notes = string.Concat(Enumerable.Repeat("Zürich 🎧 Hochzeit ", 30));
        var ics = Write(Event(notes: notes));

        foreach (var line in ics.Split("\r\n"))
        {
            Assert.True(Encoding.UTF8.GetByteCount(line) <= 75, $"{Encoding.UTF8.GetByteCount(line)} octets: {line}");
        }
        Assert.Contains(IcsCalendar.Escape(notes.Trim()), Unfolded(ics).Single(l => l.StartsWith("DESCRIPTION:")));
        Assert.DoesNotContain('�', ics);
    }

    [Fact]
    public void Tokens_are_long_random_and_url_safe()
    {
        var a = CalendarFeedToken.New();
        var b = CalendarFeedToken.New();

        Assert.NotEqual(a, b);
        Assert.Equal(43, a.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", a);
        Assert.True(a.Length <= CalendarFeedToken.MaxLength);
    }

    [Theory]
    [InlineData("secret", "secret", true)]
    [InlineData("secret", "Secret", false)]
    [InlineData("secret", "secre", false)]
    [InlineData(null, "", false)]
    [InlineData(null, null, false)]
    [InlineData("", "", false)]
    public void Token_must_match_exactly_and_an_unset_token_never_matches(string? stored, string? given, bool matches) =>
        Assert.Equal(matches, CalendarFeedToken.Matches(stored, given));

    [Fact]
    public async Task Feed_has_shoots_from_the_last_year_on_without_cancelled_ones()
    {
        var client = await _db.AddClientAsync();
        var location = await _db.AddLocationAsync();
        await _db.AddShootAsync(client, Now.AddDays(-400), Now.AddDays(-400).AddHours(5), ShootStatus.Done, title: "Too old");
        await _db.AddShootAsync(client, Now.AddDays(-30), Now.AddDays(-30).AddHours(5), ShootStatus.Done, title: "Played");
        await _db.AddShootAsync(client, Now.AddDays(10), Now.AddDays(10).AddHours(5), ShootStatus.Cancelled, title: "Cancelled");
        await _db.AddShootAsync(client, Now.AddDays(20), Now.AddDays(20).AddHours(5), ShootStatus.Inquiry, title: "Asked");
        await _db.AddShootAsync(client, Now.AddDays(5), Now.AddDays(5).AddHours(5), location: location, title: "Booked");

        await using var db = _db.CreateContext();
        var events = await CalendarFeedEndpoints.LoadAsync(db, Now);

        Assert.Equal(["Played", "Booked", "Asked"], events.Select(e => e.Title));
        var booked = events.Single(e => e.Title == "Booked");
        Assert.Equal("Schloss Laufen, Laufen-Uhwiesen", booked.Location);
        Assert.Equal("Anna & Marco Rossi", booked.ClientName);
        Assert.Null(events.Single(e => e.Title == "Played").Location);
    }

    [Fact]
    public async Task Token_is_off_until_created()
    {
        await using var db = _db.CreateContext();
        Assert.Null(await db.BusinessSettings.Select(s => s.CalendarFeedToken).SingleAsync());
    }
}
