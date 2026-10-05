namespace Funk.Web.Auth;

/// <summary>
/// The single login. Set via user-secrets in development and environment
/// variables / appsettings.Production.json on the Mac mini. Never commit the hash.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string Username { get; set; } = "";

    /// <summary>Generate with <c>dotnet run --project src/Funk.Web -- hash-password</c>.</summary>
    public string PasswordHash { get; set; } = "";
}
