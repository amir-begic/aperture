namespace Aperture.Web.Domain;

public static class LinkProvider
{
    /// <summary>"Pinterest", "Google Drive", … from a moodboard or reference URL, or "Link" if unknown.</summary>
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
            _ when Is("pinterest.com") || Is("pinterest.ch") || Is("pin.it") => "Pinterest",
            _ when Is("instagram.com") => "Instagram",
            _ when Is("drive.google.com") || Is("docs.google.com") => "Google Drive",
            _ when Is("dropbox.com") => "Dropbox",
            _ when Is("icloud.com") => "iCloud",
            _ when Is("wetransfer.com") || Is("we.tl") => "WeTransfer",
            _ when Is("canva.com") => "Canva",
            _ when Is("milanote.com") => "Milanote",
            _ when Is("notion.so") || Is("notion.site") => "Notion",
            _ => "Link",
        };
    }
}
