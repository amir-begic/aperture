using System.ComponentModel.DataAnnotations;
using Aperture.Web.Domain;

namespace Aperture.Web.Data.Entities;

public class Shoot
{
    public int Id { get; set; }

    [Required, MaxLength(150)]
    public string Title { get; set; } = "";

    public ShootStatus Status { get; set; } = ShootStatus.Inquiry;

    public ShootType ShootType { get; set; } = ShootType.Other;

    /// <summary>Local Swiss time, like everything else in the app.</summary>
    public DateTime Start { get; set; }

    /// <summary>After <see cref="Start"/>; may be on the next day.</summary>
    public DateTime End { get; set; }

    /// <summary>Unknown while the shoot is still an early inquiry.</summary>
    public Money? QuotedPrice { get; set; }

    public int ClientId { get; set; }
    public Client Client { get; set; } = null!;

    /// <summary>Often not known yet at the inquiry stage.</summary>
    public int? LocationId { get; set; }
    public Location? Location { get; set; }

    /// <summary>The package the shoot was quoted from, if any; its details were copied in and can be changed per shoot.</summary>
    public int? PackageId { get; set; }
    public Package? Package { get; set; }

    [MaxLength(4000)]
    public string? Notes { get; set; }

    /// <summary>When the client should have the final images; suggested from <see cref="BusinessSettings.DeliveryDays"/>.</summary>
    public DateOnly? DeliveryDue { get; set; }

    /// <summary>Where the client sees the images (Pixieset, Pic-Time, Dropbox, …).</summary>
    [MaxLength(500)]
    [RegularExpression(@"^https?://\S+$", ErrorMessage = "Paste a full link starting with https://")]
    public string? GalleryUrl { get; set; }

    public DateTime CreatedAt { get; set; }

    public List<Expense> Expenses { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];
    public List<ReferenceLink> ReferenceLinks { get; set; } = [];
    public List<ChecklistItem> ChecklistItems { get; set; } = [];
    public List<Attachment> Attachments { get; set; } = [];
    public List<Quote> Quotes { get; set; } = [];
    public List<Invoice> Invoices { get; set; } = [];
}
