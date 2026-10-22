using Aperture.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aperture.Web.Features.Shoots;

public enum ShootPeriod
{
    Upcoming,
    Past,
    All,
}

public static class ShootQueries
{
    /// <summary>
    /// Shoots whose time range intersects [start, end). Back-to-back shoots (one ends when the
    /// next starts) don't count, and neither do cancelled ones.
    /// </summary>
    public static IQueryable<Shoot> Overlapping(this IQueryable<Shoot> shoots, DateTime start, DateTime end, int? excludeShootId = null) =>
        shoots.InRange(start, end, includeCancelled: false).Where(g => g.Id != excludeShootId);

    /// <summary>Shoots that take place (at least partly) within [start, end), e.g. the days a calendar shows.</summary>
    public static IQueryable<Shoot> InRange(this IQueryable<Shoot> shoots, DateTime start, DateTime end, bool includeCancelled) =>
        shoots.Where(g => g.Start < end && start < g.End && (includeCancelled || g.Status != ShootStatus.Cancelled));

    /// <summary>
    /// Filters for the shoot list. Upcoming shoots (including one in progress) are sorted soonest first,
    /// everything else newest first.
    /// </summary>
    public static IQueryable<Shoot> Filter(
        this IQueryable<Shoot> shoots, ShootPeriod period, IReadOnlyCollection<ShootStatus> statuses, string? search, DateTime now)
    {
        if (statuses.Count > 0)
        {
            shoots = shoots.Where(g => statuses.Contains(g.Status));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            shoots = shoots.Where(g => EF.Functions.Like(g.Title, pattern)
                || EF.Functions.Like(g.Client.Name, pattern)
                || (g.Client.Company != null && EF.Functions.Like(g.Client.Company, pattern))
                || (g.Location != null && (EF.Functions.Like(g.Location.Name, pattern)
                    || (g.Location.City != null && EF.Functions.Like(g.Location.City, pattern)))));
        }

        return period switch
        {
            ShootPeriod.Upcoming => shoots.Where(g => g.End > now).OrderBy(g => g.Start),
            ShootPeriod.Past => shoots.Where(g => g.End <= now).OrderByDescending(g => g.Start),
            _ => shoots.OrderByDescending(g => g.Start),
        };
    }
}
