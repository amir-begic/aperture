using System.Globalization;

namespace Aperture.Web.Localization;

/// <summary>A language Aperture speaks: the app's own (Settings) and each client's, for their invoices.</summary>
public enum Language
{
    English,
    German,
}

public static class Languages
{
    // Swiss formats in both: dates as 31.12.2026, Monday as first day of the week.
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-CH");
    private static readonly CultureInfo GermanCulture = CultureInfo.GetCultureInfo("de-CH");

    public static readonly IReadOnlyList<CultureInfo> Cultures = [EnglishCulture, GermanCulture];

    public static CultureInfo Culture(this Language language) => language == Language.German ? GermanCulture : EnglishCulture;

    /// <summary>German for any German culture (de, de-DE, de-CH, …), English for everything else.</summary>
    public static Language From(CultureInfo culture) => FromCode(culture.Name);

    /// <inheritdoc cref="From(CultureInfo)"/>
    public static Language FromCode(string code) =>
        code.StartsWith("de", StringComparison.OrdinalIgnoreCase) ? Language.German : Language.English;

    /// <summary>The language the current request or job runs in.</summary>
    public static Language Current => From(CultureInfo.CurrentUICulture);

    /// <summary>The language's name in itself, as a language picker shows it.</summary>
    public static string Name(this Language language) => language == Language.German ? "Deutsch" : "English";

    /// <summary>
    /// Runs the code that follows in <paramref name="language"/> until the scope is disposed: texts, month and day names.
    /// For work outside a request, such as notifications.
    /// </summary>
    public static IDisposable Use(Language language) => new CultureScope(language.Culture());

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
        private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

        public CultureScope(CultureInfo culture) => CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;

        public void Dispose()
        {
            CultureInfo.CurrentCulture = _culture;
            CultureInfo.CurrentUICulture = _uiCulture;
        }
    }
}
