using System.ComponentModel.DataAnnotations;

namespace Aperture.Web.Data.Entities;

public class PlaylistLink
{
    public int Id { get; set; }

    public int GigId { get; set; }
    public Gig Gig { get; set; } = null!;

    [Required, MaxLength(500)]
    [RegularExpression(@"^https?://\S+$", ErrorMessage = "Paste a full link starting with https://")]
    public string Url { get; set; } = "";

    /// <summary>What it's for, e.g. "First dance" or "Do not play".</summary>
    [MaxLength(100)]
    public string? Label { get; set; }
}
