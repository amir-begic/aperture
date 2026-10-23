using System.ComponentModel.DataAnnotations;
using Aperture.Web.Domain;

namespace Aperture.Web.Data.Entities;

/// <summary>An offer for a shoot: what it costs and what's included, valid until a date. Sent before the invoice.</summary>
public class Quote : ClientDocument
{
    public int Id { get; set; }

    public int ShootId { get; set; }
    public Shoot Shoot { get; set; } = null!;

    /// <summary>"Q-2026-001"; assigned when sent, so drafts don't leave gaps in the sequence.</summary>
    public string? Number { get; set; }

    public QuoteStatus Status { get; set; } = QuoteStatus.Draft;

    public DateOnly QuoteDate { get; set; }

    public DateOnly ValidUntil { get; set; }

    public List<QuoteLine> Lines { get; set; } = [];

    /// <summary>What the price includes, printed under the lines; from the package the shoot was quoted from.</summary>
    [MaxLength(1000)]
    public string? Included { get; set; }

    public DateTime? SentAt { get; set; }

    /// <summary>When it was marked accepted or declined.</summary>
    public DateTime? AnsweredAt { get; set; }

    /// <summary>The archived PDF of a sent quote.</summary>
    public int? PdfId { get; set; }
    public Attachment? Pdf { get; set; }

    public InvoiceTotals Totals => InvoiceTotals.Calculate(
        Lines.Select(l => new InvoiceLineAmount(l.Quantity, l.UnitPrice)), VatRatePercent, Money.Zero);
}

public class QuoteLine
{
    public int Id { get; set; }

    public int QuoteId { get; set; }
    public Quote Quote { get; set; } = null!;

    public int SortOrder { get; set; }

    [Required, MaxLength(300)]
    public string Description { get; set; } = "";

    public decimal Quantity { get; set; } = 1;

    public Money UnitPrice { get; set; }

    public Money Total => UnitPrice * Quantity;
}
