using System.ComponentModel.DataAnnotations;

namespace Aperture.Web.Data.Entities;

/// <summary>
/// A device that gets notifications: the browser's push subscription (Settings → Notifications).
/// The endpoint is the push service's address for that device; the keys encrypt what Aperture sends to it.
/// </summary>
public class PushSubscription
{
    public int Id { get; set; }

    [Required, MaxLength(2048)]
    public string Endpoint { get; set; } = "";

    /// <summary>The device's public key (P-256, base64url).</summary>
    [Required, MaxLength(100)]
    public string P256dh { get; set; } = "";

    /// <summary>The device's authentication secret (16 bytes, base64url).</summary>
    [Required, MaxLength(50)]
    public string Auth { get; set; } = "";

    /// <summary>"iPhone", "Chrome on Windows": shown in Settings so old devices can be removed.</summary>
    [Required, MaxLength(100)]
    public string Device { get; set; } = "";

    public DateTime CreatedAt { get; set; }

    /// <summary>When the push service last accepted a notification for this device.</summary>
    public DateTime? LastDeliveredAt { get; set; }
}
