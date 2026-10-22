using Aperture.Web.Data.Entities;
using Aperture.Web.Domain;
using Aperture.Web.Features.Shoots;

namespace Aperture.Tests.Features;

public class ShootFormTests
{
    [Fact]
    public void Round_trips_through_a_shoot()
    {
        var form = new ShootForm
        {
            Title = "  Wedding Rossi  ",
            ClientId = 3,
            LocationId = 7,
            Status = ShootStatus.Confirmed,
            EventType = EventType.Wedding,
            Date = new DateOnly(2026, 12, 12),
            StartTime = new TimeOnly(22, 0),
            EndTime = new TimeOnly(3, 0),
            QuotedPriceChf = 1850.50m,
            Notes = "  ",
        };

        var shoot = new Shoot();
        form.ApplyTo(shoot);

        Assert.Equal("Wedding Rossi", shoot.Title);
        Assert.Equal(new DateTime(2026, 12, 13, 3, 0, 0), shoot.End);
        Assert.Equal(Money.FromChf(1850.50m), shoot.QuotedPrice);
        Assert.Null(shoot.Notes);

        var reloaded = ShootForm.From(shoot);
        Assert.Equal(form.Date, reloaded.Date);
        Assert.Equal(form.StartTime, reloaded.StartTime);
        Assert.Equal(form.EndTime, reloaded.EndTime);
        Assert.Equal(1850.50m, reloaded.QuotedPriceChf);
        Assert.Equal(7, reloaded.LocationId);
    }

    [Fact]
    public void Empty_quote_stays_empty()
    {
        var shoot = new Shoot { QuotedPrice = Money.FromChf(100) };
        new ShootForm { ClientId = 1, Date = new DateOnly(2026, 12, 12), QuotedPriceChf = null }.ApplyTo(shoot);

        Assert.Null(shoot.QuotedPrice);
    }

    [Fact]
    public void Range_is_null_without_a_date()
    {
        Assert.Null(new ShootForm().Range);
    }
}
