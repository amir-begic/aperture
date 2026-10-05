namespace Funk.Web.Auth;

public static class ReturnUrl
{
    /// <summary>
    /// Only allows app-relative paths, so a crafted login link can't send you to another site
    /// ("//evil.example" and "/\evil.example" are treated as absolute by browsers).
    /// </summary>
    public static string Sanitize(string? returnUrl) =>
        returnUrl is ['/', not '/' and not '\\', ..] or "/"
            ? returnUrl
            : "/";
}
