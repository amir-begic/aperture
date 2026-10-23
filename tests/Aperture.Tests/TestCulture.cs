using System.Globalization;
using System.Runtime.CompilerServices;

namespace Aperture.Tests;

/// <summary>Texts follow the UI culture: the tests expect English, whatever the machine they run on is set to.</summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void UseEnglish() =>
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture =
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-CH");
}
