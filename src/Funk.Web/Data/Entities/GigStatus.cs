namespace Funk.Web.Data.Entities;

public enum GigStatus
{
    /// <summary>Client asked; the date is held tentatively.</summary>
    Inquiry,

    /// <summary>Booked.</summary>
    Confirmed,

    /// <summary>Played.</summary>
    Done,

    /// <summary>Kept for the records, ignored for overlaps.</summary>
    Cancelled,
}
