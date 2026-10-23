using Aperture.Web.Domain;

namespace Aperture.Web.Data.Entities;

public class Invoice : ClientDocument
{
    public int Id { get; set; }

    public int ShootId { get; set; }
    public Shoot Shoot { get; set; } = null!;

    /// <summary>"2026-001"; assigned when issued, so drafts don't leave gaps in the sequence.</summary>
    public string? Number { get; set; }

    public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

    public DateOnly IssueDate { get; set; }

    public DateOnly DueDate { get; set; }

    public List<InvoiceLine> Lines { get; set; } = [];

    // ---- Frozen when issued, with the seller and client (see ClientDocument) ----

    public string? SellerIban { get; set; }

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
