using System.Text;
using Aperture.Web.Data.Entities;
using Aperture.Web.Features.Calendar;

namespace Aperture.Tests.Features;

public class ShootCalendarTests : IAsyncLifetime
{
    private readonly TestDatabase _db = new();

    public Task InitializeAsync() => _db.InitializeAsync();

    public Task DisposeAsync() => _db.DisposeAsync();

    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0);

    private static CalendarEvent Event(
        ShootStatus status = ShootStatus.Booked, string title = "Wedding Rossi", string? notes = null,
        ShootType type = ShootType.Wedding, string? location = "Schloss Laufen, 8447 Laufen-Uhwiesen") =>
        new(7, title, status, type, new DateTime(2026, 12, 12, 20, 0, 0), new DateTime(2026, 12, 13, 2, 0, 0),
            "Anna & Marco Rossi", "+41 79 123 45 67", location, notes);

    private static string Write(params CalendarEvent[] events) =>
        IcsCalendar.Write(events, "http://localhost:5090/", new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc));

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
        Assert.Contains("URL:http://localhost:5090/shoots/7", lines);
        Assert.Contains("STATUS:CONFIRMED", lines);
        Assert.Contains("SUMMARY:Wedding Rossi", lines);
        Assert.Contains("LOCATION:Schloss Laufen\\, 8447 Laufen-Uhwiesen", lines);
    }

    [Fact]
    public void Description_has_status_client_phone_and_notes()
    {
        var description = Unfolded(Write(Event(notes: "Ceremony 14:00; couple shoot at \"the old bridge\"")))
            .Single(l => l.StartsWith("DESCRIPTION:"));

        Assert.Equal(
            "DESCRIPTION:Booked · Wedding\\nAnna & Marco Rossi · +41 79 123 45 67\\n\\nCeremony 14:00\\; couple shoot at \"the old bridge\"\\n\\nhttp://localhost:5090/shoots/7",
            description);
    }

    [Fact]
    public void Inquiries_are_tentative_and_marked_in_the_title()
    {
        var lines = Unfolded(Write(Event(ShootStatus.Inquiry, type: ShootType.Other)));

        Assert.Contains("STATUS:TENTATIVE", lines);
        Assert.Contains("SUMMARY:Wedding Rossi (inquiry)", lines);
        Assert.Contains(lines, l => l.StartsWith("DESCRIPTION:Inquiry\\n"));
    }

    [Fact]
    public void Shoot_without_location_leaves_LOCATION_out()
    {
        Assert.DoesNotContain("LOCATION:", Write(Event(location: null)));
    }

    [Fact]
    public void Leaves_the_choice_of_calendar_to_the_calendar_app()
    {
        var ics = Write(Event());

        Assert.DoesNotContain("X-WR-CALNAME", ics);
        Assert.DoesNotContain("REFRESH-INTERVAL", ics);
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
    public async Task Event_has_the_client_and_the_location_address()
    {
        var client = await _db.AddClientAsync();
        var location = await _db.AddLocationAsync();
        var booked = await _db.AddShootAsync(client, Now.AddDays(5), Now.AddDays(5).AddHours(5), location: location, title: "Booked");
        var other = await _db.AddShootAsync(client, Now.AddDays(6), Now.AddDays(6).AddHours(5), title: "Other");

        await using var db = _db.CreateContext();
        var shoot = Assert.Single(await db.Shoots.Where(s => s.Id == booked.Id).ToCalendarEventsAsync());
        var withoutLocation = Assert.Single(await db.Shoots.Where(s => s.Id == other.Id).ToCalendarEventsAsync());

        Assert.Equal("Booked", shoot.Title);
        Assert.Equal("Schloss Laufen, Laufen-Uhwiesen", shoot.Location);
        Assert.Equal("Anna & Marco Rossi", shoot.ClientName);
        Assert.Null(withoutLocation.Location);
    }
}
