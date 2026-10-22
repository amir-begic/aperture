using Aperture.Web.Data;
using Aperture.Web.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aperture.Web.Features.CalendarFeed;

public static class CalendarFeedEndpoints
{
    /// <summary>How far back played shoots stay in the feed.</summary>
    public static readonly TimeSpan History = TimeSpan.FromDays(365);

    /// <summary>
    /// GET /calendar/{token}.ics: the shoots as a calendar subscription, for the phone's calendar app.
    /// Anonymous, since calendar apps can't sign in; the secret token in the URL is the key (see Settings).
    /// Unknown or revoked tokens get a 404, the same as a missing page.
    /// </summary>
    public static void MapCalendarFeedEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/calendar/{token}.ics", async (string token, HttpRequest request,
                IDbContextFactory<ApertureDbContext> dbFactory, TimeProvider time) =>
            {
                await using var db = await dbFactory.CreateDbContextAsync();
                var stored = await db.BusinessSettings.Select(s => s.CalendarFeedToken).SingleAsync();
                if (!CalendarFeedToken.Matches(stored, token))
                {
                    return Results.NotFound();
                }

                var events = await LoadAsync(db, time.GetLocalNow().DateTime);
                var appUrl = $"{request.Scheme}://{request.Host}{request.PathBase}/";
                var ics = IcsCalendar.Write(events, appUrl, time.GetUtcNow().UtcDateTime);

                request.HttpContext.Response.Headers.CacheControl = "no-cache";
                return Results.Text(ics, IcsCalendar.ContentType);
            })
            .AllowAnonymous()
            // A plain 404 for calendar apps, not the app's not-found page (which would redirect to the login).
            .WithMetadata(new SkipStatusCodePagesAttribute());
    }

    /// <summary>Shoots from the last year onwards, without cancelled ones, in date order.</summary>
    public static async Task<List<FeedEvent>> LoadAsync(ApertureDbContext db, DateTime now)
    {
        var from = now - History;
        var shoots = await db.Shoots.AsNoTracking()
            .Where(g => g.Status != ShootStatus.Cancelled && g.End >= from)
            .OrderBy(g => g.Start)
            .Select(g => new
            {
                g.Id, g.Title, g.Status, g.EventType, g.Start, g.End, g.Notes,
                ClientName = g.Client.Name, ClientPhone = g.Client.Phone,
                Location = g.Location == null ? null : new { g.Location.Name, g.Location.Street, g.Location.PostalCode, g.Location.City },
            })
            .ToListAsync();

        return shoots.Select(g => new FeedEvent(
                g.Id, g.Title, g.Status, g.EventType, g.Start, g.End, g.ClientName, g.ClientPhone,
                g.Location is null ? null : Address(g.Location.Name, g.Location.Street, g.Location.PostalCode, g.Location.City),
                g.Notes))
            .ToList();
    }

    /// <summary>"Schloss Laufen, Schlossweg 1, 8447 Laufen-Uhwiesen": enough for the phone's maps link.</summary>
    private static string Address(string name, string? street, string? postalCode, string? city)
    {
        var place = string.Join(" ", new[] { postalCode, city }.Where(p => !string.IsNullOrWhiteSpace(p)));
        return string.Join(", ", new[] { name, street, place }.Where(p => !string.IsNullOrWhiteSpace(p)));
    }
}
