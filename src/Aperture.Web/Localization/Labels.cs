using Aperture.Web.Data.Entities;

namespace Aperture.Web.Localization;

/// <summary>How statuses and types are shown, in the current language.</summary>
public static class Labels
{
    public static string Text(this ShootStatus status) => status switch
    {
        ShootStatus.Inquiry => Texts.Get("Inquiry"),
        ShootStatus.Booked => Texts.Get("Booked"),
        ShootStatus.Shot => Texts.Get("Shot"),
        ShootStatus.Delivered => Texts.Get("Delivered"),
        _ => Texts.Get("ShootStatus.Cancelled"),
    };

    public static string Text(this InvoiceStatus status) => status switch
    {
        InvoiceStatus.Draft => Texts.Get("Draft"),
        InvoiceStatus.Issued => Texts.Get("Issued"),
        _ => Texts.Get("InvoiceStatus.Cancelled"),
    };

    public static string Text(this QuoteStatus status) => status switch
    {
        QuoteStatus.Draft => Texts.Get("Draft"),
        QuoteStatus.Sent => Texts.Get("Sent"),
        QuoteStatus.Accepted => Texts.Get("Accepted"),
        _ => Texts.Get("Declined"),
    };

    public static string Text(this ShootType type) => type switch
    {
        ShootType.Wedding => Texts.Get("Wedding"),
        ShootType.Portrait => Texts.Get("Portrait"),
        ShootType.Family => Texts.Get("Family"),
        ShootType.Event => Texts.Get("Event"),
        ShootType.Corporate => Texts.Get("Corporate"),
        ShootType.Commercial => Texts.Get("Commercial"),
        _ => Texts.Get("Other"),
    };

    public static string Text(this PaymentMethod method) => method switch
    {
        PaymentMethod.BankTransfer => Texts.Get("Bank transfer"),
        PaymentMethod.Cash => Texts.Get("Cash"),
        PaymentMethod.Twint => "TWINT",
        _ => Texts.Get("Other"),
    };

    /// <summary>Values with their names, for dropdowns: <c>TextProperty="Text" ValueProperty="Value"</c>.</summary>
    public static IReadOnlyList<Choice<T>> Choices<T>(Func<T, string> text) where T : struct, Enum =>
        Enum.GetValues<T>().Select(value => new Choice<T>(value, text(value))).ToList();
}

/// <summary>A dropdown entry.</summary>
public sealed record Choice<T>(T Value, string Text);
