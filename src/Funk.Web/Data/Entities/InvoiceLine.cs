using System.ComponentModel.DataAnnotations;
using Funk.Web.Domain;

namespace Funk.Web.Data.Entities;

public class InvoiceLine
{
    public int Id { get; set; }

    public int InvoiceId { get; set; }
    public Invoice Invoice { get; set; } = null!;

    public int SortOrder { get; set; }

    [Required, MaxLength(300)]
    public string Description { get; set; } = "";

    public decimal Quantity { get; set; } = 1;

    public Money UnitPrice { get; set; }

    public Money Total => UnitPrice * Quantity;
}
