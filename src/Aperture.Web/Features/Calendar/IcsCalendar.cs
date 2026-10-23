using System.Globalization;
using System.Text;
using Aperture.Web.Data.Entities;
using Aperture.Web.Localization;

namespace Aperture.Web.Features.Calendar;

/// <summary>One shoot as a calendar event. Times are local Swiss time, like everywhere in the app.</summary>
public sealed record CalendarEvent(
    int ShootId,
    string Title,
    ShootStatus Status,
    ShootType ShootType,
    DateTime Start,
    DateTime End,
    string ClientName,
    string? ClientPhone,
    string? Location,
    string? Notes);

/// <summary>
/// Writes shoots as iCalendar (RFC 5545), to open in a calendar app (Add to calendar on a shoot). Inquiries are
/// tentative, everything else confirmed. Times carry the Europe/Zurich time zone, so calendars show them right whatever
/// zone they're in. No calendar name: the calendar app adds the shoot to a calendar of the user's choosing.
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

    /// <param name="appUrl">Base URL of the app ending in "/", for the link back to each shoot.</param>
    /// <param name="now">For DTSTAMP, in UTC.</param>
    public static string Write(IEnumerable<CalendarEvent> events, string appUrl, DateTime now)
    {
        var ics = new StringBuilder();
        void Line(string line) => AppendFolded(ics, line);

        Line("BEGIN:VCALENDAR");
        Line("VERSION:2.0");
        Line("PRODID:-//Aperture//Photo office//EN");
        Line("CALSCALE:GREGORIAN");
        Line("METHOD:PUBLISH");
        foreach (var line in ZurichTimeZone.Split('\n'))
        {
            Line(line.TrimEnd('\r'));
        }

        var stamp = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        foreach (var e in events)
        {
            var tentative = e.Status == ShootStatus.Inquiry;
            var url = $"{appUrl}shoots/{e.ShootId}";

            Line("BEGIN:VEVENT");
            Line($"UID:shoot-{e.ShootId}@aperture");
            Line($"DTSTAMP:{stamp}");
            Line($"DTSTART;TZID=Europe/Zurich:{Local(e.Start)}");
            Line($"DTEND;TZID=Europe/Zurich:{Local(e.End)}");
            Line($"SUMMARY:{Escape(tentative ? Texts.Format("{0} (inquiry)", e.Title) : e.Title)}");
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

    private static string Description(CalendarEvent e, string url)
    {
        var lines = new List<string>
        {
            e.ShootType == ShootType.Other ? e.Status.Text() : $"{e.Status.Text()} · {e.ShootType.Text()}",
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
