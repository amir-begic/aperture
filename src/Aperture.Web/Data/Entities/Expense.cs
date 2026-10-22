using System.ComponentModel.DataAnnotations;
using Aperture.Web.Domain;

namespace Aperture.Web.Data.Entities;

/// <summary>A cost, such as renting lights for a gig.</summary>
public class Expense
{
    public int Id { get; set; }

    /// <summary>Null for general business costs (added in a later phase).</summary>
    public int? GigId { get; set; }
    public Gig? Gig { get; set; }

    public DateOnly Date { get; set; }

    public ExpenseCategory Category { get; set; } = ExpenseCategory.GearRental;

    [Required, MaxLength(150)]
    public string Description { get; set; } = "";

    [MaxLength(150)]
    public string? Vendor { get; set; }

    /// <summary>What it cost you.</summary>
    public Money Amount { get; set; }

    /// <summary>
    /// What the client is charged for it as a separate invoice line on top of the quote:
    /// the same as <see cref="Amount"/> to pass it on at cost, more to add your margin.
    /// Null when the cost is covered by the quote.
    /// </summary>
    public Money? BilledToClient { get; set; }

    public int? ReceiptId { get; set; }
    public Attachment? Receipt { get; set; }
}
