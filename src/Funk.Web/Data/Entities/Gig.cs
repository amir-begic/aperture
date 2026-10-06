using System.ComponentModel.DataAnnotations;
using Funk.Web.Domain;

namespace Funk.Web.Data.Entities;

public class Gig
{
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string Title { get; set; } = "";

    public GigStatus Status { get; set; } = GigStatus.Inquiry;

    public EventType EventType { get; set; } = EventType.Other;

    /// <summary>Local Swiss time, like everything else in the app.</summary>
    public DateTime Start { get; set; }

    /// <summary>After <see cref="Start"/>; may be on the next day.</summary>
    public DateTime End { get; set; }

    /// <summary>Unknown while the gig is still an early inquiry.</summary>
    public Money? QuotedPrice { get; set; }

    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    /// <summary>Often not known yet at the inquiry stage.</summary>
    public int? VenueId { get; set; }
    public Venue? Venue { get; set; }

    [MaxLength(4000)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<Expense> Expenses { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];
    public List<PlaylistLink> PlaylistLinks { get; set; } = [];
    public List<ChecklistItem> ChecklistItems { get; set; } = [];
    public List<Attachment> Attachments { get; set; } = [];
    public List<Invoice> Invoices { get; set; } = [];
}
