using Aperture.Web.Data.Entities;

namespace Aperture.Web.Domain;

public static class ChecklistTemplates
{
    public static readonly IReadOnlyList<string> Wedding =
    [
        "Timeline from couple / wedding planner received",
        "Contact person on the day confirmed",
        "Family group list received",
        "Must-have shots and moodboard received",
        "Getting-ready locations and times known",
        "Photo rules for the ceremony checked (officiant, flash, where to stand)",
        "Couple shoot spot and time planned, with a bad-weather option",
        "Second shooter booked",
        "Meals, breaks, travel and parking clarified",
        "Batteries charged, cards formatted, backup body packed",
    ];

    public static readonly IReadOnlyList<string> Portrait =
    [
        "Moodboard agreed",
        "Outfit and styling tips sent",
        "Location or studio booked, with a bad-weather option",
        "Number of edited images agreed",
        "Model release signed (if images are used for marketing)",
        "Batteries charged, cards formatted",
    ];

    public static readonly IReadOnlyList<string> Commercial =
    [
        "Brief and shot list received",
        "Usage rights agreed in writing",
        "Deliverables agreed (number of images, formats, sizes)",
        "Location access and permits arranged",
        "Models booked, releases ready",
        "Props and styling organised",
        "Batteries charged, cards formatted, backup body packed",
    ];

    public static readonly IReadOnlyList<string> Event =
    [
        "Run sheet and timings received",
        "Contact person on the day confirmed",
        "Key people and must-have moments listed",
        "Access, dress code and photo rules checked (stage, flash, guests who don't want photos)",
        "Same-day selection or social media images agreed",
        "Batteries charged, cards formatted, backup body packed",
    ];

    public static readonly IReadOnlyList<(string Name, IReadOnlyList<string> Items)> All =
    [
        ("Wedding", Wedding),
        ("Portrait", Portrait),
        ("Event", Event),
        ("Commercial", Commercial),
    ];

    /// <summary>The template that fits a shoot type, offered first on the prep tab; null for <see cref="ShootType.Other"/>.</summary>
    public static string? For(ShootType type) => type switch
    {
        ShootType.Wedding => "Wedding",
        ShootType.Portrait or ShootType.Family => "Portrait",
        ShootType.Event => "Event",
        ShootType.Corporate or ShootType.Commercial => "Commercial",
        _ => null,
    };
}
