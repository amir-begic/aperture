using System.ComponentModel.DataAnnotations;
using Aperture.Web.Domain;

namespace Aperture.Web.Data.Entities;

public class Client
{
    public int Id { get; set; }

    /// <summary>Person or couple, e.g. "Anna &amp; Marco Rossi".</summary>
    [Required, MaxLength(150)]
    public string Name { get; set; } = "";

    [MaxLength(150)]
    public string? Company { get; set; }

    [OptionalPhone, MaxLength(50)]
    public string? Phone { get; set; }

    [OptionalEmail, MaxLength(200)]
    public string? Email { get; set; }

    [MaxLength(100)]
    public string? Street { get; set; }

    [MaxLength(16)]
    public string? PostalCode { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [Required, RegularExpression("^[A-Z]{2}$", ErrorMessage = "Use a two-letter country code, e.g. CH.")]
    public string CountryCode { get; set; } = "CH";

    [MaxLength(4000)]
    public string? Notes { get; set; }

    public List<Gig> Gigs { get; set; } = [];
}
