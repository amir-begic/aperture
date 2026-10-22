using System.ComponentModel.DataAnnotations;

namespace Aperture.Web.Data.Entities;

public class Location
{
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string Name { get; set; } = "";

    [MaxLength(100)]
    public string? Street { get; set; }

    [MaxLength(16)]
    public string? PostalCode { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    /// <summary>Load-in, parking, tech contact, power, …</summary>
    [MaxLength(4000)]
    public string? Notes { get; set; }

    public List<Shoot> Shoots { get; set; } = [];
}
