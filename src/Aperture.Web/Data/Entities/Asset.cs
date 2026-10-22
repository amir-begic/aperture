using System.ComponentModel.DataAnnotations;
using Aperture.Web.Domain;

namespace Aperture.Web.Data.Entities;

/// <summary>
/// Equipment bought for the business and used over several years (cameras, lenses, lights, laptop).
/// Its cost is spread over the years as depreciation instead of being a cost in the year of purchase.
/// </summary>
public class Asset
{
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = "";

    public AssetCategory Category { get; set; } = AssetCategory.Camera;

    public DateOnly PurchaseDate { get; set; }

    public Money Cost { get; set; }

    /// <summary>Declining-balance rate in % of the book value per year.</summary>
    [Range(0, 100)]
    public decimal DepreciationRatePercent { get; set; } = 30;

    [MaxLength(150)]
    public string? Vendor { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    public int? ReceiptId { get; set; }
    public Attachment? Receipt { get; set; }

    /// <summary>Sold or thrown away; depreciation stops and the rest of the book value is settled.</summary>
    public DateOnly? DisposedOn { get; set; }

    public Money? SaleProceeds { get; set; }

    public IReadOnlyList<DepreciationYear> Schedule(int untilYear, bool proRataFirstYear) =>
        Depreciation.Schedule(PurchaseDate, Cost, DepreciationRatePercent, DisposedOn, SaleProceeds, untilYear, proRataFirstYear);
}
