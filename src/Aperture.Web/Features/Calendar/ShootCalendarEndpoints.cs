using System.Text;
using Aperture.Web.Data;
using Aperture.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aperture.Web.Features.Calendar;

public static class ShootCalendarEndpoints
{
    /// <summary>
    /// GET /shoots/{id}.ics: one shoot to open in a calendar app (Add to calendar on the shoot). With a synced calendar it
    /// reaches the phone too. Requires sign-in like every other page.
    /// </summary>
    public static void MapShootCalendarEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/shoots/{id:int}.ics", async (int id, HttpRequest request,
            IDbContextFactory<ApertureDbContext> dbFactory, TimeProvider time) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var events = await db.Shoots.Where(s => s.Id == id).ToCalendarEventsAsync();
            if (events.Count == 0)
            {
                return Results.NotFound();
            }

            var appUrl = $"{request.Scheme}://{request.Host}{request.PathBase}/";
            var ics = IcsCalendar.Write(events, appUrl, time.GetUtcNow().UtcDateTime);
            return Results.File(Encoding.UTF8.GetBytes(ics), IcsCalendar.ContentType, $"shoot-{id}.ics");
        });
    }

    public static async Task<List<CalendarEvent>> ToCalendarEventsAsync(this IQueryable<Shoot> query)
    {
        var shoots = await query.AsNoTracking()
            .Select(s => new
            {
                s.Id, s.Title, s.Status, s.ShootType, s.Start, s.End, s.Notes,
                ClientName = s.Client.Name, ClientPhone = s.Client.Phone,
                Location = s.Location == null ? null : new { s.Location.Name, s.Location.Street, s.Location.PostalCode, s.Location.City },
            })
            .ToListAsync();

        return shoots.Select(s => new CalendarEvent(
                s.Id, s.Title, s.Status, s.ShootType, s.Start, s.End, s.ClientName, s.ClientPhone,
                s.Location is null ? null : Address(s.Location.Name, s.Location.Street, s.Location.PostalCode, s.Location.City),
                s.Notes))
            .ToList();
    }

    /// <summary>"Schloss Laufen, Schlossweg 1, 8447 Laufen-Uhwiesen": enough for the calendar's maps link.</summary>
    private static string Address(string name, string? street, string? postalCode, string? city)
    {
        var place = string.Join(" ", new[] { postalCode, city }.Where(p => !string.IsNullOrWhiteSpace(p)));
        return string.Join(", ", new[] { name, street, place }.Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}
