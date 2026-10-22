using Aperture.Web.Domain;

namespace Aperture.Tests.Domain;

public class LinkProviderTests
{
    [Theory]
    [InlineData("https://www.pinterest.com/anna/wedding-ideas/", "Pinterest")]
    [InlineData("https://ch.pinterest.com/anna/wedding-ideas/", "Pinterest")]
    [InlineData("https://pin.it/abc123", "Pinterest")]
    [InlineData("https://www.instagram.com/p/abc123/", "Instagram")]
    [InlineData("https://drive.google.com/drive/folders/1", "Google Drive")]
    [InlineData("https://docs.google.com/document/d/1/edit", "Google Drive")]
    [InlineData("https://www.dropbox.com/scl/fo/1", "Dropbox")]
    [InlineData("https://we.tl/t-abc", "WeTransfer")]
    [InlineData("https://www.canva.com/design/1/view", "Canva")]
    [InlineData("https://anna.notion.site/Shot-list-1", "Notion")]
    [InlineData("https://example.com/notpinterest.com", "Link")]
    [InlineData("https://evilpinterest.com/x", "Link")]
    [InlineData("not a url", "Link")]
    public void Detects_provider_from_host(string url, string expected)
    {
        Assert.Equal(expected, LinkProvider.FromUrl(url));
    }
}
