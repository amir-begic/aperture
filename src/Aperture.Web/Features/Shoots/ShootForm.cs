using System.ComponentModel.DataAnnotations;
using Aperture.Web.Data.Entities;
using Aperture.Web.Domain;

namespace Aperture.Web.Features.Shoots;

/// <summary>
/// What the shoot form edits. Separate from <see cref="Shoot"/> because the form works with
/// a date plus two times and a CHF amount, while the entity stores a range and <see cref="Money"/>.
/// </summary>
public sealed class ShootForm
{
    [Required, MaxLength(150)]
    public string Title { get; set; } = "";

    [Required(ErrorMessage = "Choose a client.")]
    public int? ClientId { get; set; }

    public int? LocationId { get; set; }

    public int? PackageId { get; set; }

    public ShootStatus Status { get; set; } = ShootStatus.Inquiry;

    public ShootType ShootType { get; set; } = ShootType.Other;

    [Required(ErrorMessage = "Choose a date.")]
    public DateOnly? Date { get; set; }

    public TimeOnly StartTime { get; set; } = new(10, 0);

    public TimeOnly EndTime { get; set; } = new(14, 0);

    [Range(typeof(decimal), "0", "1000000", ParseLimitsInInvariantCulture = true)]
    public decimal? QuotedPriceChf { get; set; }

    [MaxLength(4000)]
    public string? Notes { get; set; }

    public DateOnly? DeliveryDue { get; set; }

    [MaxLength(500)]
    [RegularExpression(@"^https?://\S+$", ErrorMessage = "Paste a full link starting with https://")]
    public string? GalleryUrl { get; set; }

    public bool EndsNextDay => ShootSchedule.EndsNextDay(StartTime, EndTime);

    /// <summary>Null until a date is chosen.</summary>
    public (DateTime Start, DateTime End)? Range =>
        Date is { } date ? ShootSchedule.ToRange(date, StartTime, EndTime) : null;

    public static ShootForm From(Shoot shoot) => new()
    {
        Title = shoot.Title,
        ClientId = shoot.ClientId,
        LocationId = shoot.LocationId,
        PackageId = shoot.PackageId,
        Status = shoot.Status,
        ShootType = shoot.ShootType,
        Date = DateOnly.FromDateTime(shoot.Start),
        StartTime = TimeOnly.FromDateTime(shoot.Start),
        EndTime = TimeOnly.FromDateTime(shoot.End),
        QuotedPriceChf = shoot.QuotedPrice?.Chf,
        Notes = shoot.Notes,
        DeliveryDue = shoot.DeliveryDue,
        GalleryUrl = shoot.GalleryUrl,
    };

    /// <summary>
    /// Sets the delivery date <paramref name="deliveryDays"/> after the shoot. Called when the date changes: a delivery
    /// date that's empty or was suggested for <paramref name="previousDate"/> moves along, one set by hand stays.
    /// </summary>
    public void SuggestDeliveryDue(DateOnly? previousDate, int deliveryDays)
    {
        if (DeliveryDue is null || DeliveryDue == previousDate?.AddDays(deliveryDays))
        {
            DeliveryDue = Date?.AddDays(deliveryDays);
        }
    }

    /// <summary>
    /// Fills in what the package sets: type, quote, end time, delivery date (the package's delivery time, or
    /// <paramref name="defaultDeliveryDays"/>) and, if there are no notes yet, what's included.
    /// </summary>
    public void ApplyPackage(Package package, int defaultDeliveryDays)
    {
        PackageId = package.Id;
        ShootType = package.ShootType;
        QuotedPriceChf = package.Price.Chf;
        EndTime = StartTime.Add(TimeSpan.FromHours((double)package.Hours));
        DeliveryDue = Date?.AddDays(package.DeliveryDays ?? defaultDeliveryDays);
        if (string.IsNullOrWhiteSpace(Notes))
        {
            Notes = package.Included;
        }
    }

    public void ApplyTo(Shoot shoot)
    {
        var (start, end) = Range ?? throw new InvalidOperationException("A shoot needs a date.");

        shoot.Title = Title.Trim();
        shoot.ClientId = ClientId ?? throw new InvalidOperationException("A shoot needs a client.");
        shoot.LocationId = LocationId;
        shoot.PackageId = PackageId;
        shoot.Status = Status;
        shoot.ShootType = ShootType;
        shoot.Start = start;
        shoot.End = end;
        shoot.QuotedPrice = QuotedPriceChf is { } chf ? Money.FromChf(chf) : null;
        shoot.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
        shoot.DeliveryDue = DeliveryDue;
        shoot.GalleryUrl = string.IsNullOrWhiteSpace(GalleryUrl) ? null : GalleryUrl.Trim();
    }
}
