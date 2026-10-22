using Aperture.Web.Domain;

namespace Aperture.Web.Data.Entities;

public class Invoice
{
    public int Id { get; set; }

    public int GigId { get; set; }
    public Gig Gig { get; set; } = null!;

    /// <summary>"2026-001"; assigned when issued, so drafts don't leave gaps in the sequence.</summary>
    public string? Number { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    public DateOnly IssueDate { get; set; }

    public DateOnly DueDate { get; set; }

    public List<InvoiceLine> Lines { get; set; } = [];

    // ---- Frozen when issued ----

    public InvoiceAddress Seller { get; set; } = new();

    public string? SellerIban { get; set; }
    public string? SellerEmail { get; set; }
    public string? SellerPhone { get; set; }
    public string? SellerVatNumber { get; set; }

    /// <summary>Null when not registered for VAT.</summary>
    public decimal? VatRatePercent { get; set; }

    public InvoiceAddress Client { get; set; } = new();

    /// <summary>Payments received before the invoice was issued (e.g. a deposit).</summary>
    public Money AlreadyPaid { get; set; }

    public DateTime? IssuedAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    /// <summary>The archived PDF of an issued invoice.</summary>
    public int? PdfId { get; set; }
    public Attachment? Pdf { get; set; }

    public InvoiceTotals Totals => InvoiceTotals.Calculate(
        Lines.Select(l => new InvoiceLineAmount(l.Quantity, l.UnitPrice)), VatRatePercent, AlreadyPaid);

    public string DisplayNumber => Number ?? "Draft";
}
