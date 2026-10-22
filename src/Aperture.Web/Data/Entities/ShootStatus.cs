namespace Aperture.Web.Data.Entities;

public enum ShootStatus
{
    /// <summary>Client asked; the date is held tentatively.</summary>
    Inquiry,

    /// <summary>Confirmed by the client.</summary>
    Booked,

    /// <summary>Photographed; the images are still being edited.</summary>
    Shot,

    /// <summary>The client has the final images.</summary>
    Delivered,

    /// <summary>Kept for the records, ignored for overlaps.</summary>
    Cancelled,
}

public static class ShootStatuses
{
    /// <summary>The shoot has happened, whether or not the images are delivered yet.</summary>
    public static bool HasTakenPlace(this ShootStatus status) => status is ShootStatus.Shot or ShootStatus.Delivered;

    /// <summary>Booked or shot, so the client is still waiting for the images.</summary>
    public static bool AwaitsDelivery(this ShootStatus status) => status is ShootStatus.Booked or ShootStatus.Shot;
}
