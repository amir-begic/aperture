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
        shoots.InRange(start, end, includeCancelled: false).Where(s => s.Id != excludeShootId);

    /// <summary>Shoots that take place (at least partly) within [start, end), e.g. the days a calendar shows.</summary>
    public static IQueryable<Shoot> InRange(this IQueryable<Shoot> shoots, DateTime start, DateTime end, bool includeCancelled) =>
        shoots.Where(s => s.Start < end && start < s.End && (includeCancelled || s.Status != ShootStatus.Cancelled));

    /// <summary>
    /// Filters for the shoot list. Upcoming shoots (including one in progress) are sorted soonest first,
    /// everything else newest first.
    /// </summary>
    public static IQueryable<Shoot> Filter(
        this IQueryable<Shoot> shoots, ShootPeriod period, IReadOnlyCollection<ShootStatus> statuses, string? search, DateTime now)
    {
        if (statuses.Count > 0)
        {
            shoots = shoots.Where(s => statuses.Contains(s.Status));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            shoots = shoots.Where(s => EF.Functions.Like(s.Title, pattern)
                || EF.Functions.Like(s.Client.Name, pattern)
                || (s.Client.Company != null && EF.Functions.Like(s.Client.Company, pattern))
                || (s.Location != null && (EF.Functions.Like(s.Location.Name, pattern)
                    || (s.Location.City != null && EF.Functions.Like(s.Location.City, pattern)))));
        }

        return period switch
        {
            ShootPeriod.Upcoming => shoots.Where(s => s.End > now).OrderBy(s => s.Start),
            ShootPeriod.Past => shoots.Where(s => s.End <= now).OrderByDescending(s => s.Start),
            _ => shoots.OrderByDescending(s => s.Start),
        };
    }
}
