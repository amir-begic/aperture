using Aperture.Web.Localization;

namespace Aperture.Web.Data.Entities;

/// <summary>
/// What quotes and invoices have in common: who sends them to whom, in which language, with or without VAT.
/// Filled in from your settings and the client for drafts, and frozen once the document goes out (see InvoiceSnapshot).
/// </summary>
public abstract class ClientDocument
{
    public InvoiceAddress Seller { get; set; } = new();

    public string? SellerEmail { get; set; }
    public string? SellerPhone { get; set; }
    public string? SellerVatNumber { get; set; }

    /// <summary>Null when not registered for VAT.</summary>
    public decimal? VatRatePercent { get; set; }

    public InvoiceAddress Client { get; set; } = new();

    /// <summary>What the document is written in: chosen on the draft, Aperture's language unless changed.</summary>
    public Language Language { get; set; }
}
