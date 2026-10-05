using Funk.Web.Auth;

namespace Funk.Tests.Auth;

public class ReturnUrlTests
{
    [Theory]
    [InlineData("/", "/")]
    [InlineData("/settings", "/settings")]
    [InlineData("/gigs/12?tab=music", "/gigs/12?tab=music")]
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
