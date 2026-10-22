using System.Globalization;
using System.Text;
using Aperture.Web.Data.Entities;

namespace Aperture.Web.Features.CalendarFeed;

/// <summary>One gig as a calendar event. Times are local Swiss time, like everywhere in the app.</summary>
public sealed record FeedEvent(
    int GigId,
    string Title,
    GigStatus Status,
    EventType EventType,
    DateTime Start,
    DateTime End,
    string ClientName,
    string? ClientPhone,
    string? Location,
    string? Notes);

/// <summary>
/// Writes gigs as an iCalendar (RFC 5545) subscription feed: inquiries are tentative, everything else confirmed.
/// Times carry the Europe/Zurich time zone, so phones show them right whatever zone they're in.
/// </summary>
public static class IcsCalendar
{
    public const string ContentType = "text/calendar; charset=utf-8";

    // Central European Time with the EU summer time rules (last Sunday of March / October).
    private const string ZurichTimeZone =
        """
        BEGIN:VTIMEZONE
        TZID:Europe/Zurich
        BEGIN:DAYLIGHT
        TZOFFSETFROM:+0100
        TZOFFSETTO:+0200
        TZNAME:CEST
        DTSTART:19700329T020000
        RRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=-1SU
        END:DAYLIGHT
        BEGIN:STANDARD
        TZOFFSETFROM:+0200
        TZOFFSETTO:+0100
        TZNAME:CET
        DTSTART:19701025T030000
        RRULE:FREQ=YEARLY;BYMONTH=10;BYDAY=-1SU
        END:STANDARD
        END:VTIMEZONE
        """;

    /// <param name="appUrl">Base URL of the app ending in "/", for the link back to each gig.</param>
    /// <param name="now">For DTSTAMP, in UTC.</param>
    public static string Write(IEnumerable<FeedEvent> events, string appUrl, DateTime now)
    {
        var ics = new StringBuilder();
        void Line(string line) => AppendFolded(ics, line);

        Line("BEGIN:VCALENDAR");
        Line("VERSION:2.0");
        Line("PRODID:-//Aperture//DJ office//EN");
        Line("CALSCALE:GREGORIAN");
        Line("METHOD:PUBLISH");
        Line("X-WR-CALNAME:Aperture gigs");
        Line("X-WR-TIMEZONE:Europe/Zurich");
        // Hints for how often to refresh; Apple and Google mostly decide themselves.
        Line("REFRESH-INTERVAL;VALUE=DURATION:PT1H");
        Line("X-PUBLISHED-TTL:PT1H");
        foreach (var line in ZurichTimeZone.Split('\n'))
        {
            Line(line.TrimEnd('\r'));
        }

        var stamp = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        foreach (var e in events)
        {
            var tentative = e.Status == GigStatus.Inquiry;
            var url = $"{appUrl}gigs/{e.GigId}";

            Line("BEGIN:VEVENT");
            Line($"UID:gig-{e.GigId}@aperture");
            Line($"DTSTAMP:{stamp}");
            Line($"DTSTART;TZID=Europe/Zurich:{Local(e.Start)}");
            Line($"DTEND;TZID=Europe/Zurich:{Local(e.End)}");
            Line($"SUMMARY:{Escape(tentative ? $"{e.Title} (inquiry)" : e.Title)}");
            Line($"STATUS:{(tentative ? "TENTATIVE" : "CONFIRMED")}");
            if (!string.IsNullOrWhiteSpace(e.Location))
            {
                Line($"LOCATION:{Escape(e.Location)}");
            }
            Line($"DESCRIPTION:{Escape(Description(e, url))}");
            Line($"URL:{url}");
            Line("END:VEVENT");
        }

        Line("END:VCALENDAR");
        return ics.ToString();
    }

    private static string Description(FeedEvent e, string url)
    {
        var lines = new List<string>
        {
            e.EventType == EventType.Other ? $"{e.Status}" : $"{e.Status} · {e.EventType}",
            string.IsNullOrWhiteSpace(e.ClientPhone) ? e.ClientName : $"{e.ClientName} · {e.ClientPhone}",
        };
        if (!string.IsNullOrWhiteSpace(e.Notes))
        {
            lines.Add("");
            lines.Add(e.Notes.Trim());
        }
        lines.Add("");
        lines.Add(url);
        return string.Join("\n", lines);
    }

    private static string Local(DateTime time) => time.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);

    /// <summary>TEXT values: backslash, semicolon, comma and line breaks are escaped (RFC 5545 3.3.11).</summary>
    public static string Escape(string text) => text
        .Replace("\\", "\\\\")
        .Replace(";", "\\;")
        .Replace(",", "\\,")
        .Replace("\r\n", "\n")
        .Replace("\r", "\n")
        .Replace("\n", "\\n");

    /// <summary>
    /// Lines end in CRLF and are folded at 75 octets (UTF-8), continuing with a space (RFC 5545 3.1).
    /// Never splits a character, so umlauts and emoji survive.
    /// </summary>
    private static void AppendFolded(StringBuilder ics, string line)
    {
        const int limit = 75;
        var octets = 0;
        var enumerator = StringInfo.GetTextElementEnumerator(line);
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            var size = Encoding.UTF8.GetByteCount(element);
            if (octets + size > limit)
            {
                ics.Append("\r\n ");
                octets = 1;
            }
            ics.Append(element);
            octets += size;
        }
        ics.Append("\r\n");
    }
}
