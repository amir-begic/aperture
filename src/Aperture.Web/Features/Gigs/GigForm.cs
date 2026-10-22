using System.ComponentModel.DataAnnotations;
using Aperture.Web.Data.Entities;
using Aperture.Web.Domain;

namespace Aperture.Web.Features.Gigs;

/// <summary>
/// What the gig form edits. Separate from <see cref="Gig"/> because the form works with
/// a date plus two times and a CHF amount, while the entity stores a range and <see cref="Money"/>.
/// </summary>
public sealed class GigForm
{
    [Required, MaxLength(150)]
    public string Title { get; set; } = "";

    [Required(ErrorMessage = "Choose a client.")]
    public int? ClientId { get; set; }

    public int? VenueId { get; set; }

    public GigStatus Status { get; set; } = GigStatus.Inquiry;

    public EventType EventType { get; set; } = EventType.Other;

    [Required(ErrorMessage = "Choose a date.")]
    public DateOnly? Date { get; set; }

    public TimeOnly StartTime { get; set; } = new(20, 0);

    public TimeOnly EndTime { get; set; } = new(2, 0);

    [Range(typeof(decimal), "0", "1000000", ParseLimitsInInvariantCulture = true)]
    public decimal? QuotedPriceChf { get; set; }

    [MaxLength(4000)]
    public string? Notes { get; set; }

    public bool EndsNextDay => GigSchedule.EndsNextDay(StartTime, EndTime);

    /// <summary>Null until a date is chosen.</summary>
    public (DateTime Start, DateTime End)? Range =>
        Date is { } date ? GigSchedule.ToRange(date, StartTime, EndTime) : null;

    public static GigForm From(Gig gig) => new()
    {
        Title = gig.Title,
        ClientId = gig.ClientId,
        VenueId = gig.VenueId,
        Status = gig.Status,
        EventType = gig.EventType,
        Date = DateOnly.FromDateTime(gig.Start),
        StartTime = TimeOnly.FromDateTime(gig.Start),
        EndTime = TimeOnly.FromDateTime(gig.End),
        QuotedPriceChf = gig.QuotedPrice?.Chf,
        Notes = gig.Notes,
    };

    public void ApplyTo(Gig gig)
    {
        var (start, end) = Range ?? throw new InvalidOperationException("A gig needs a date.");

        gig.Title = Title.Trim();
        gig.ClientId = ClientId ?? throw new InvalidOperationException("A gig needs a client.");
        gig.VenueId = VenueId;
        gig.Status = Status;
        gig.EventType = EventType;
        gig.Start = start;
        gig.End = end;
        gig.QuotedPrice = QuotedPriceChf is { } chf ? Money.FromChf(chf) : null;
        gig.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
    }
}
