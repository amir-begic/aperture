using Aperture.Web.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Aperture.Tests.Localization;

public sealed class AppLanguageTests
{
    [Theory]
    [InlineData(null, "en-CH")]
    [InlineData("de-DE,de;q=0.9,en;q=0.8", "de-CH")]
    [InlineData("fr-CH,de-CH;q=0.8,en;q=0.5", "de-CH")]   // the first one Aperture speaks
    [InlineData("en-GB,en;q=0.9,de;q=0.8", "en-CH")]
    [InlineData("en;q=0.5,de-CH", "de-CH")]                 // by quality, not order
    [InlineData("fr-FR,it;q=0.8", "en-CH")]
    public async Task Until_a_language_is_chosen_pages_follow_the_browser(string? acceptLanguage, string expected)
    {
        var result = await CultureFor(chosen: null, acceptLanguage);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task The_language_chosen_in_settings_wins_over_the_browser()
    {
        Assert.Equal("en-CH", await CultureFor(Language.English, "de-CH,de;q=0.9"));
        Assert.Equal("de-CH", await CultureFor(Language.German, "en-US"));
    }

    private static async Task<string> CultureFor(Language? chosen, string? acceptLanguage)
    {
        var language = new AppLanguage(dbFactory: null!);
        language.Set(chosen);
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(language).BuildServiceProvider(),
        };
        if (acceptLanguage is not null)
        {
            context.Request.Headers.AcceptLanguage = acceptLanguage;
        }

        var result = await new AppLanguageCultureProvider().DetermineProviderCultureResult(context);
        return result!.UICultures.Single().Value!;
    }
}
