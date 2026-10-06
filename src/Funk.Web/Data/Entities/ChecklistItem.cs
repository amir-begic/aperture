using System.ComponentModel.DataAnnotations;

namespace Funk.Web.Data.Entities;

/// <summary>A preparation task for a gig, e.g. "First dance song confirmed".</summary>
public class ChecklistItem
{
    public int Id { get; set; }

    public int GigId { get; set; }
    public Gig Gig { get; set; } = null!;

    [Required, MaxLength(200)]
    public string Text { get; set; } = "";

    public bool IsDone { get; set; }

    public int SortOrder { get; set; }
}
