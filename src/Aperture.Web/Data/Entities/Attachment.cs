using System.ComponentModel.DataAnnotations;

namespace Aperture.Web.Data.Entities;

/// <summary>An uploaded file. The bytes live on disk under the data directory, not in the database.</summary>
public class Attachment
{
    public int Id { get; set; }

    /// <summary>The shoot the file belongs to, including receipts and payslips of its costs and payments.</summary>
    public int? ShootId { get; set; }
    public Shoot? Shoot { get; set; }

    /// <summary>The name it was uploaded with, used for downloads.</summary>
    [Required, MaxLength(255)]
    public string FileName { get; set; } = "";

    [Required, MaxLength(100)]
    public string ContentType { get; set; } = "";

    public long SizeBytes { get; set; }

    /// <summary>Relative to the attachments directory, e.g. "2026/3f2c….pdf".</summary>
    [Required, MaxLength(300)]
    public string StoragePath { get; set; } = "";

    public DateTime UploadedAt { get; set; }
}
