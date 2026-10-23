using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Aperture.Web.Localization;

namespace Aperture.Tests.Localization;

public sealed partial class TextsTests
{
    private static readonly CultureInfo German = Language.German.Culture();
    private static readonly CultureInfo English = Language.English.Culture();

    [Fact]
    public void German_texts_keep_the_placeholders_of_the_english_ones()
    {
        var resources = new ResourceManager(typeof(Texts));
        var german = resources.GetResourceSet(CultureInfo.GetCultureInfo("de"), createIfNotExists: true, tryParents: false)!;

        var mismatches = german.Cast<DictionaryEntry>()
            .Select(entry => (Key: (string)entry.Key, English: Texts.Get((string)entry.Key, English), German: (string)entry.Value!))
            .Where(text => !Placeholders(text.English).SetEquals(Placeholders(text.German)))
            .Select(text => $"{text.Key} → {text.German}")
            .ToList();

        Assert.Empty(mismatches);
    }

    [Fact]
    public void Texts_are_looked_up_by_their_english_text()
    {
        Assert.Equal("Rechnungen", Texts.Get("Invoices", German));
        Assert.Equal("Invoices", Texts.Get("Invoices", English));
        Assert.Equal("Not translated (yet)", Texts.Get("Not translated (yet)", German));
    }

    [Fact]
    public void Context_keys_tell_apart_what_english_says_the_same_way()
    {
        Assert.Equal("Cancelled", Texts.Get("ShootStatus.Cancelled", English));
        Assert.Equal("Cancelled", Texts.Get("InvoiceStatus.Cancelled", English));
        Assert.Equal("Abgesagt", Texts.Get("ShootStatus.Cancelled", German));
        Assert.Equal("Storniert", Texts.Get("InvoiceStatus.Cancelled", German));
    }

    [Fact]
    public void Format_uses_the_current_language()
    {
        using (Languages.Use(Language.German))
        {
            Assert.Equal("Rechnung 2026-001 ist überfällig", Texts.Format("Invoice {0} is overdue", "2026-001"));
            Assert.Equal("Fällig Mo 12.10.2026 · Anna", Texts.Format("Due {0:ddd dd.MM.yyyy} · {1}", new DateOnly(2026, 10, 12), "Anna"));
        }

        Assert.Equal("Invoice 2026-001 is overdue", Texts.Format("Invoice {0} is overdue", "2026-001"));
    }

    private static HashSet<string> Placeholders(string text) => Placeholder().Matches(text).Select(m => m.Groups[1].Value).ToHashSet();

    [GeneratedRegex(@"\{(\d+)")]
    private static partial Regex Placeholder();
}
