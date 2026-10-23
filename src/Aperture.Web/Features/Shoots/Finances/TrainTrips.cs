using Aperture.Web.Data;
using Aperture.Web.Data.Entities;
using Aperture.Web.Domain;
using Aperture.Web.Localization;
using Microsoft.EntityFrameworkCore;

namespace Aperture.Web.Features.Shoots.Finances;

/// <summary>Pre-filled values for a new cost (see ExpenseDialog).</summary>
public sealed record ExpensePreset(ExpenseCategory Category, string Description, string? Vendor, Money? Amount);

/// <summary>
/// Getting to a shoot by train, entered as a travel cost with SBB as the vendor (Add train trip on the shoot's Finances
/// tab). The fare is suggested from the last train trip to the same location, since it's usually the same ticket.
/// </summary>
public static class TrainTrips
{
    public const string Vendor = "SBB";

    public static async Task<ExpensePreset> SuggestAsync(ApertureDbContext db, int shootId)
    {
        var shoot = await db.Shoots.AsNoTracking()
            .Where(s => s.Id == shootId)
            .Select(s => new { s.LocationId, LocationName = s.Location == null ? null : s.Location.Name, City = s.Location == null ? null : s.Location.City })
            .SingleAsync();

        var lastFare = shoot.LocationId is null ? null : await db.Expenses
            .Where(e => e.ShootId != shootId && e.Shoot!.LocationId == shoot.LocationId
                && e.Category == ExpenseCategory.Travel && e.Vendor == Vendor)
            .OrderByDescending(e => e.Date).ThenByDescending(e => e.Id)
            .Select(e => (Money?)e.Amount)
            .FirstOrDefaultAsync();

        return new ExpensePreset(ExpenseCategory.Travel, Description(shoot.City ?? shoot.LocationName), Vendor, lastFare);
    }

    /// <summary>"Train to Laufen-Uhwiesen and back"; without a location, "Train to the shoot and back". In the current language.</summary>
    public static string Description(string? place) =>
        string.IsNullOrWhiteSpace(place) ? Texts.Get("Train to the shoot and back") : Texts.Format("Train to {0} and back", place.Trim());
}
