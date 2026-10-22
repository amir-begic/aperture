using Aperture.Web.Auth;

namespace Aperture.Tests.Auth;

public class ReturnUrlTests
{
    [Theory]
    [InlineData("/", "/")]
    [InlineData("/settings", "/settings")]
    [InlineData("/shoots/12?tab=prep", "/shoots/12?tab=prep")]
    [InlineData(null, "/")]
    [InlineData("", "/")]
    [InlineData("settings", "/")]
    [InlineData("https://evil.example", "/")]
    [InlineData("//evil.example", "/")]
    [InlineData(@"/\evil.example", "/")]
    public void Sanitize_only_allows_local_paths(string? input, string expected)
    {
        Assert.Equal(expected, ReturnUrl.Sanitize(input));
    }
}
