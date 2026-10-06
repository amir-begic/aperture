namespace Funk.Web.Data.Entities;

public enum InvoiceStatus
{
    /// <summary>Editable, no number yet.</summary>
    Draft,

    /// <summary>Numbered and frozen; the PDF is archived.</summary>
    Issued,

    /// <summary>Withdrawn; keeps its number so the sequence has no gaps.</summary>
    Cancelled,
}
