namespace Aperture.Web.Data.Entities;

/// <summary>
/// A sender or recipient address as printed on an invoice. Copied when the invoice is issued,
/// so later changes to your settings or the client don't alter issued invoices.
/// Stored as columns of the invoice (an EF Core complex type).
/// </summary>
public class InvoiceAddress
{
    public string Name { get; set; } = "";
    public string? Company { get; set; }
    public string? Street { get; set; }
    public string? PostalCode { get; set; }
    public string? City { get; set; }
    public string CountryCode { get; set; } = "CH";

    public bool HasPostalAddress =>
        !string.IsNullOrWhiteSpace(Street) && !string.IsNullOrWhiteSpace(PostalCode) && !string.IsNullOrWhiteSpace(City);
}
