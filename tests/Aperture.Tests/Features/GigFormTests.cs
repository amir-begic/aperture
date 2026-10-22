using Aperture.Web.Data.Entities;
using Aperture.Web.Domain;
using Aperture.Web.Features.Gigs;

namespace Aperture.Tests.Features;

public class GigFormTests
{
    [Fact]
    public void Round_trips_through_a_gig()
    {
        var form = new GigForm
        {
            Title = "  Wedding Rossi  ",
            ClientId = 3,
            VenueId = 7,
            Status = GigStatus.Confirmed,
            EventType = EventType.Wedding,
            Date = new DateOnly(2026, 12, 12),
            StartTime = new TimeOnly(22, 0),
            EndTime = new TimeOnly(3, 0),
            QuotedPriceChf = 1850.50m,
            Notes = "  ",
        };

        var gig = new Gig();
        form.ApplyTo(gig);

        Assert.Equal("Wedding Rossi", gig.Title);
        Assert.Equal(new DateTime(2026, 12, 13, 3, 0, 0), gig.End);
        Assert.Equal(Money.FromChf(1850.50m), gig.QuotedPrice);
        Assert.Null(gig.Notes);

        var reloaded = GigForm.From(gig);
        Assert.Equal(form.Date, reloaded.Date);
        Assert.Equal(form.StartTime, reloaded.StartTime);
        Assert.Equal(form.EndTime, reloaded.EndTime);
        Assert.Equal(1850.50m, reloaded.QuotedPriceChf);
        Assert.Equal(7, reloaded.VenueId);
    }

    [Fact]
    public void Empty_quote_stays_empty()
    {
        var gig = new Gig { QuotedPrice = Money.FromChf(100) };
        new GigForm { ClientId = 1, Date = new DateOnly(2026, 12, 12), QuotedPriceChf = null }.ApplyTo(gig);

        Assert.Null(gig.QuotedPrice);
    }

    [Fact]
    public void Range_is_null_without_a_date()
    {
        Assert.Null(new GigForm().Range);
    }
}
