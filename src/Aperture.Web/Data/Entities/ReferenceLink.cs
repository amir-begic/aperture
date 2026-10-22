using System.ComponentModel.DataAnnotations;

namespace Aperture.Web.Data.Entities;

/// <summary>A moodboard, shot list or other reference for a shoot.</summary>
public class ReferenceLink
{
    public int Id { get; set; }

    public int ShootId { get; set; }
    public Shoot Shoot { get; set; } = null!;

    [Required, MaxLength(500)]
    [RegularExpression(@"^https?://\S+$", ErrorMessage = "Paste a full link starting with https://")]
    public string Url { get; set; } = "";

    /// <summary>What it's for, e.g. "Moodboard" or "Family group list".</summary>
    [MaxLength(100)]
    public string? Label { get; set; }
}
