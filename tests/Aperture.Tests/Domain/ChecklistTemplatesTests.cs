using Aperture.Web.Data.Entities;
using Aperture.Web.Domain;

namespace Aperture.Tests.Domain;

public class ChecklistTemplatesTests
{
    [Theory]
    [InlineData(ShootType.Wedding, "Wedding")]
    [InlineData(ShootType.Family, "Portrait")]
    [InlineData(ShootType.Event, "Event")]
    [InlineData(ShootType.Corporate, "Commercial")]
    [InlineData(ShootType.Other, null)]
    public void Suggests_the_template_that_fits_the_shoot_type(ShootType type, string? expected) =>
        Assert.Equal(expected, ChecklistTemplates.For(type));

    [Fact]
    public void Every_suggestion_is_a_template_that_exists()
    {
        var names = ChecklistTemplates.All.Select(t => t.Name).ToHashSet();

        foreach (var type in Enum.GetValues<ShootType>())
        {
            if (ChecklistTemplates.For(type) is { } name)
            {
                Assert.Contains(name, names);
            }
        }
    }
}
