using QuestPDF.Drawing;

namespace Aperture.Web.Features.Invoices;

/// <summary>
/// The invoice font, embedded in the app so PDFs look identical on every machine. QuestPDF doesn't
/// use system fonts (since 2026.9), and the QR-bill standard only allows Arial, Frutiger, Helvetica
/// or Liberation Sans, the one that may be redistributed.
/// </summary>
public static class InvoiceFonts
{
    public const string Family = "Liberation Sans";

    private static readonly Lazy<bool> Registered = new(() =>
    {
        var assembly = typeof(InvoiceFonts).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.Ordinal)))
        {
            using var font = assembly.GetManifestResourceStream(name)!;
            FontManager.RegisterFontFromStream(font);
        }
        return true;
    });

    public static void EnsureRegistered() => _ = Registered.Value;
}
