using Aperture.Web.Data.Entities;

namespace Aperture.Web.Domain;

/// <summary>
/// Equipment categories with the ESTV's maximum declining-balance rates (Merkblatt A 1995, % of book value).
/// They're defaults: each asset keeps its own rate, and lower rates are always allowed.
/// </summary>
public static class AssetCategories
{
    public static readonly IReadOnlyList<(AssetCategory Value, string Text, decimal DefaultRatePercent, string EstvClass)> All =
    [
        (AssetCategory.Camera, "Cameras & lenses", 30, "Apparate und Maschinen"),
        (AssetCategory.Lighting, "Lighting & studio", 30, "Apparate und Maschinen"),
        (AssetCategory.Computer, "Computer & software", 40, "Datenverarbeitungsanlagen (Hardware und Software)"),
        (AssetCategory.Vehicle, "Vehicle", 40, "Motorfahrzeuge aller Art"),
        (AssetCategory.Furniture, "Furniture, bags & cases", 25, "Geschäftsmobiliar"),
        (AssetCategory.Other, "Other", 30, "Apparate und Maschinen"),
    ];

    public static string Text(this AssetCategory category) => All.First(c => c.Value == category).Text;

    public static decimal DefaultRatePercent(this AssetCategory category) => All.First(c => c.Value == category).DefaultRatePercent;
}
