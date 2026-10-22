namespace Aperture.Web.Domain;

public static class PlaylistProvider
{
    /// <summary>"Spotify", "Tidal", … from a playlist URL, or "Link" if unknown.</summary>
    public static string FromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return "Link";
        }

        var host = uri.Host.ToLowerInvariant();
        bool Is(string domain) => host == domain || host.EndsWith("." + domain);

        return true switch
        {
            _ when Is("spotify.com") || Is("spotify.link") => "Spotify",
            _ when Is("tidal.com") => "Tidal",
            _ when Is("music.apple.com") => "Apple Music",
            _ when Is("music.youtube.com") => "YouTube Music",
            _ when Is("youtube.com") || Is("youtu.be") => "YouTube",
            _ when Is("soundcloud.com") => "SoundCloud",
            _ when Is("deezer.com") => "Deezer",
            _ => "Link",
        };
    }
}
