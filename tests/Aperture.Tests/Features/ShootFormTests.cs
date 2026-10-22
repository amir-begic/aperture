using Aperture.Web.Data.Entities;
using Aperture.Web.Domain;
using Aperture.Web.Features.Shoots;

namespace Aperture.Tests.Features;

public class ShootFormTests
{
    private static readonly Package FullDay = new()
    {
        Id = 4, Name = "Wedding full day", ShootType = ShootType.Wedding, Price = Money.FromChf(3800), Hours = 10.5m,
        DeliveryDays = 42, Included = "Second shooter, 400 edited images",
    };

    [Fact]
    public void Package_fills_in_type_quote_end_delivery_and_notes()
    {
        var form = new ShootForm { Date = new DateOnly(2027, 6, 5), StartTime = new TimeOnly(13, 0) };

        form.ApplyPackage(FullDay, defaultDeliveryDays: 28);

        Assert.Equal(4, form.PackageId);
        Assert.Equal(ShootType.Wedding, form.ShootType);
        Assert.Equal(3800m, form.QuotedPriceChf);
        Assert.Equal(new TimeOnly(23, 30), form.EndTime);
        Assert.Equal(new DateOnly(2027, 7, 17), form.DeliveryDue);
        Assert.Equal("Second shooter, 400 edited images", form.Notes);
    }

    [Fact]
    public void Package_keeps_existing_notes_and_falls_back_to_the_usual_delivery_time()
    {
        var form = new ShootForm { Date = new DateOnly(2027, 6, 5), Notes = "Ceremony at 14:00" };

        form.ApplyPackage(new Package { Id = 5, Name = "Portrait", Price = Money.FromChf(450), Hours = 1.5m, Included = "20 images" },
            defaultDeliveryDays: 28);

        Assert.Equal("Ceremony at 14:00", form.Notes);
        Assert.Equal(new DateOnly(2027, 7, 3), form.DeliveryDue);
        Assert.Equal(new TimeOnly(11, 30), form.EndTime);
    }

    [Fact]
    public void Round_trips_through_a_shoot()
    {
        var form = new ShootForm
        {
            Title = "  Wedding Rossi  ",
            ClientId = 3,
            LocationId = 7,
            Status = ShootStatus.Booked,
            ShootType = ShootType.Wedding,
            Date = new DateOnly(2026, 12, 12),
            StartTime = new TimeOnly(22, 0),
            EndTime = new TimeOnly(3, 0),
            QuotedPriceChf = 1850.50m,
            Notes = "  ",
            DeliveryDue = new DateOnly(2027, 1, 9),
            GalleryUrl = " https://rossi.pixieset.com/wedding/ ",
        };

        var shoot = new Shoot();
        form.ApplyTo(shoot);

        Assert.Equal("Wedding Rossi", shoot.Title);
        Assert.Equal(new DateTime(2026, 12, 13, 3, 0, 0), shoot.End);
        Assert.Equal(Money.FromChf(1850.50m), shoot.QuotedPrice);
        Assert.Null(shoot.Notes);
        Assert.Equal("https://rossi.pixieset.com/wedding/", shoot.GalleryUrl);

        var reloaded = ShootForm.From(shoot);
        Assert.Equal(form.Date, reloaded.Date);
        Assert.Equal(form.StartTime, reloaded.StartTime);
        Assert.Equal(form.EndTime, reloaded.EndTime);
        Assert.Equal(1850.50m, reloaded.QuotedPriceChf);
        Assert.Equal(7, reloaded.LocationId);
        Assert.Equal(new DateOnly(2027, 1, 9), reloaded.DeliveryDue);
    }

    [Fact]
    public void Suggested_delivery_follows_the_date_until_changed_by_hand()
    {
        var form = new ShootForm { Date = new DateOnly(2026, 12, 12) };
        form.SuggestDeliveryDue(previousDate: null, deliveryDays: 28);
        Assert.Equal(new DateOnly(2027, 1, 9), form.DeliveryDue);

        form.Date = new DateOnly(2026, 12, 19);
        form.SuggestDeliveryDue(previousDate: new DateOnly(2026, 12, 12), deliveryDays: 28);
        Assert.Equal(new DateOnly(2027, 1, 16), form.DeliveryDue);

        form.DeliveryDue = new DateOnly(2027, 1, 31);
        form.Date = new DateOnly(2026, 12, 26);
        form.SuggestDeliveryDue(previousDate: new DateOnly(2026, 12, 19), deliveryDays: 28);
        Assert.Equal(new DateOnly(2027, 1, 31), form.DeliveryDue);
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
