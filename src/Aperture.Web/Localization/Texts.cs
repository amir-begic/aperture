using System.Globalization;
using System.Resources;

namespace Aperture.Web.Localization;

/// <summary>
/// Aperture's texts. The English text is the key, so English needs no resource file; the German is in Texts.de.resx.
/// Where one English word needs two German ones ("Cancelled": a shoot is abgesagt, an invoice storniert), the key
/// names its context and Texts.resx holds the English.
/// Pages use it as <c>IStringLocalizer&lt;Texts&gt;</c> (<c>L["…"]</c>), other code through <see cref="Get"/> and <see cref="Format"/>.
/// </summary>
public sealed class Texts
{
    private static readonly ResourceManager Resources = new(typeof(Texts));

    /// <summary>The text in <paramref name="culture"/>, by default the current one; the key itself if it isn't translated.</summary>
    public static string Get(string key, CultureInfo? culture = null) =>
        Resources.GetString(key, culture ?? CultureInfo.CurrentUICulture) ?? key;

    /// <summary><see cref="Get"/> with <c>{0}</c>, <c>{1}</c>, … filled in, formatted for the current culture.</summary>
    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), args);
}
