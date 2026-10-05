using Funk.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Funk.Web.Features.Gigs;

public enum GigPeriod
{
    Upcoming,
    Past,
    All,
}

public static class GigQueries
{
    /// <summary>
    /// Gigs whose time range intersects [start, end). Back-to-back gigs (one ends when the
    /// next starts) don't count, and neither do cancelled ones.
    /// </summary>
    public static IQueryable<Gig> Overlapping(this IQueryable<Gig> gigs, DateTime start, DateTime end, int? excludeGigId = null) =>
        gigs.Where(g => g.Status != GigStatus.Cancelled
            && g.Id != excludeGigId
            && g.Start < end
            && start < g.End);

    /// <summary>
    /// Filters for the gig list. Upcoming gigs (including one in progress) are sorted soonest first,
    /// everything else newest first.
    /// </summary>
    public static IQueryable<Gig> Filter(
        this IQueryable<Gig> gigs, GigPeriod period, IReadOnlyCollection<GigStatus> statuses, string? search, DateTime now)
    {
        if (statuses.Count > 0)
        {
            gigs = gigs.Where(g => statuses.Contains(g.Status));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            gigs = gigs.Where(g => EF.Functions.Like(g.Title, pattern)
                || EF.Functions.Like(g.Client.Name, pattern)
                || (g.Client.Company != null && EF.Functions.Like(g.Client.Company, pattern))
                || (g.Venue != null && (EF.Functions.Like(g.Venue.Name, pattern)
                    || (g.Venue.City != null && EF.Functions.Like(g.Venue.City, pattern)))));
        }

        return period switch
        {
            GigPeriod.Upcoming => gigs.Where(g => g.End > now).OrderBy(g => g.Start),
            GigPeriod.Past => gigs.Where(g => g.End <= now).OrderByDescending(g => g.Start),
            _ => gigs.OrderByDescending(g => g.Start),
        };
    }
}
