using System.ComponentModel.DataAnnotations;

namespace Aperture.Web.Data.Entities;

/// <summary>
/// A reminder that went out, so it goes out only once (see PushReminders).
/// The key names what it was about, e.g. "invoice-overdue:12".
/// </summary>
public class SentNotification
{
    [Key, MaxLength(200)]
    public string Key { get; set; } = "";

    public DateTime SentAt { get; set; }
}
