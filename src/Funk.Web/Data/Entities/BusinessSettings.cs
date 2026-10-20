using System.ComponentModel.DataAnnotations;
using Funk.Web.Domain;

namespace Funk.Web.Data.Entities;

/// <summary>Your own business details, printed on invoices. There is exactly one row.</summary>
public class BusinessSettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>Optional stage or company name, e.g. "DJ Funk".</summary>
    [MaxLength(100)]
    public string BusinessName { get; set; } = "";

    [Required, MaxLength(100)]
    public string OwnerName { get; set; } = "";

    [Required, MaxLength(100)]
    public string Street { get; set; } = "";

    [Required, MaxLength(16)]
    public string PostalCode { get; set; } = "";

    [Required, MaxLength(100)]
    public string City { get; set; } = "";

    /// <summary>ISO 3166 alpha-2 code, as required by the Swiss QR-bill.</summary>
    [Required, RegularExpression("^[A-Z]{2}$", ErrorMessage = "Use a two-letter country code, e.g. CH.")]
    public string CountryCode { get; set; } = "CH";

    [OptionalEmail, MaxLength(200)]
    public string? Email { get; set; }

    [OptionalPhone, MaxLength(50)]
    public string? Phone { get; set; }

    [MaxLength(200)]
    public string? Website { get; set; }

    /// <summary>Stored without spaces.</summary>
    [Required, ValidIban, MaxLength(34)]
    public string Iban { get; set; } = "";

    [Range(0, 365)]
    public int PaymentTermsDays { get; set; } = 30;

    /// <summary>MWST registration is mandatory from CHF 100'000 yearly turnover.</summary>
    public bool IsVatRegistered { get; set; }

    [RegularExpression(@"^CHE-\d{3}\.\d{3}\.\d{3}( MWST| TVA| IVA)?$",
        ErrorMessage = "Format: CHE-123.456.789 MWST")]
    [MaxLength(32)]
    public string? VatNumber { get; set; }

    [Range(0, 100)]
    public decimal VatRatePercent { get; set; } = 8.1m;

    /// <summary>
    /// Depreciate equipment by month in the year of purchase instead of the full annual rate.
    /// Off by default: St. Gallen allows the full rate (StB 41 Nr. 1), and so do many cantons.
    /// </summary>
    public bool ProRataFirstYear { get; set; }

    /// <summary>
    /// Secret part of the calendar feed URL (/calendar/{token}.ics); null while the feed is off.
    /// Managed on its own in Settings, never by the settings form.
    /// </summary>
    [MaxLength(Features.CalendarFeed.CalendarFeedToken.MaxLength)]
    public string? CalendarFeedToken { get; set; }

    /// <summary>True once everything an invoice needs has been filled in.</summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(OwnerName)
        && !string.IsNullOrWhiteSpace(Street)
        && !string.IsNullOrWhiteSpace(PostalCode)
        && !string.IsNullOrWhiteSpace(City)
        && Domain.Iban.IsValid(Iban)
        && (!IsVatRegistered || !string.IsNullOrWhiteSpace(VatNumber));
}
