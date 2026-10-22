using Aperture.Web.Domain;

namespace Aperture.Tests.Domain;

public class PlaylistProviderTests
{
    [Theory]
    [InlineData("https://open.spotify.com/playlist/37i9dQZF1DXcBWIGoYBM5M", "Spotify")]
    [InlineData("https://spotify.link/abc", "Spotify")]
    [InlineData("https://tidal.com/browse/playlist/123", "Tidal")]
    [InlineData("https://listen.tidal.com/playlist/123", "Tidal")]
    [InlineData("https://music.apple.com/ch/playlist/x/pl.123", "Apple Music")]
    [InlineData("https://music.youtube.com/playlist?list=1", "YouTube Music")]
    [InlineData("https://www.youtube.com/playlist?list=1", "YouTube")]
    [InlineData("https://youtu.be/abc", "YouTube")]
    [InlineData("https://example.com/notspotify.com", "Link")]
    [InlineData("https://evilspotify.com/x", "Link")]
    [InlineData("not a url", "Link")]
    public void Detects_provider_from_host(string url, string expected)
    {
        Assert.Equal(expected, PlaylistProvider.FromUrl(url));
    }
}
