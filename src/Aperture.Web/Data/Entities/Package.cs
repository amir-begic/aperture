using System.ComponentModel.DataAnnotations;
using Aperture.Web.Domain;

namespace Aperture.Web.Data.Entities;

/// <summary>An offer from your price list, e.g. "Wedding full day". Picking one on a shoot fills in its details.</summary>
public class Package
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = "";

    public ShootType ShootType { get; set; } = ShootType.Other;

    public Money Price { get; set; }

    /// <summary>How long you're on site; sets the shoot's end time.</summary>
    [Range(typeof(decimal), "0.5", "24", ParseLimitsInInvariantCulture = true)]
    public decimal Hours { get; set; } = 4;

    /// <summary>Days until delivery for this package; null for your usual delivery time (Settings).</summary>
    [Range(0, 365)]
    public int? DeliveryDays { get; set; }

    /// <summary>What the client gets, e.g. "10 hours, 400 edited images, online gallery". Goes into the shoot's notes.</summary>
    [MaxLength(500)]
    public string? Included { get; set; }

    public List<Shoot> Shoots { get; set; } = [];
}
