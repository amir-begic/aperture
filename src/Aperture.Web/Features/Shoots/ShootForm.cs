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

    public ShootStatus Status { get; set; } = ShootStatus.Inquiry;

    public EventType EventType { get; set; } = EventType.Other;

    [Required(ErrorMessage = "Choose a date.")]
    public DateOnly? Date { get; set; }

    public TimeOnly StartTime { get; set; } = new(20, 0);

    public TimeOnly EndTime { get; set; } = new(2, 0);

    [Range(typeof(decimal), "0", "1000000", ParseLimitsInInvariantCulture = true)]
    public decimal? QuotedPriceChf { get; set; }

    [MaxLength(4000)]
    public string? Notes { get; set; }

    public bool EndsNextDay => ShootSchedule.EndsNextDay(StartTime, EndTime);

    /// <summary>Null until a date is chosen.</summary>
    public (DateTime Start, DateTime End)? Range =>
        Date is { } date ? ShootSchedule.ToRange(date, StartTime, EndTime) : null;

    public static ShootForm From(Shoot shoot) => new()
    {
        Title = shoot.Title,
        ClientId = shoot.ClientId,
        LocationId = shoot.LocationId,
        Status = shoot.Status,
        EventType = shoot.EventType,
        Date = DateOnly.FromDateTime(shoot.Start),
        StartTime = TimeOnly.FromDateTime(shoot.Start),
        EndTime = TimeOnly.FromDateTime(shoot.End),
        QuotedPriceChf = shoot.QuotedPrice?.Chf,
        Notes = shoot.Notes,
    };

    public void ApplyTo(Shoot shoot)
    {
        var (start, end) = Range ?? throw new InvalidOperationException("A shoot needs a date.");

        shoot.Title = Title.Trim();
        shoot.ClientId = ClientId ?? throw new InvalidOperationException("A shoot needs a client.");
        shoot.LocationId = LocationId;
        shoot.Status = Status;
        shoot.EventType = EventType;
        shoot.Start = start;
        shoot.End = end;
        shoot.QuotedPrice = QuotedPriceChf is { } chf ? Money.FromChf(chf) : null;
        shoot.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
    }
}
