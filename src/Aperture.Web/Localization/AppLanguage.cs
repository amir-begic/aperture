using Aperture.Web.Data;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;

namespace Aperture.Web.Localization;

/// <summary>
/// The language chosen in Settings, kept in memory: every request asks for it, static files included.
/// Null until one is chosen; pages then follow the browser.
/// </summary>
public sealed class AppLanguage(IDbContextFactory<ApertureDbContext> dbFactory)
{
    public Language? Chosen { get; private set; }

    /// <summary>For work outside a request (notifications): the chosen language, English until there is one.</summary>
    public Language ForBackground => Chosen ?? Language.English;

    public async Task LoadAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        Chosen = await db.BusinessSettings.Select(s => s.Language).SingleAsync();
    }

    /// <summary>After Settings are saved. Pages already open keep their language until they're reloaded.</summary>
    public void Set(Language? language) => Chosen = language;
}

/// <summary>Each request runs in the chosen language, or in the browser's first one Aperture speaks.</summary>
public sealed class AppLanguageCultureProvider : RequestCultureProvider
{
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        var language = httpContext.RequestServices.GetRequiredService<AppLanguage>().Chosen ?? FromBrowser(httpContext.Request);
        var culture = language.Culture().Name;
        return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(culture, culture));
    }

    private static Language FromBrowser(HttpRequest request)
    {
        var preferred = request.GetTypedHeaders().AcceptLanguage
            .OrderByDescending(l => l.Quality ?? 1)
            .Select(l => l.Value.Value ?? "")
            .FirstOrDefault(l => l.StartsWith("de", StringComparison.OrdinalIgnoreCase) || l.StartsWith("en", StringComparison.OrdinalIgnoreCase));
        return preferred is null ? Language.English : Languages.FromCode(preferred);
    }
}
