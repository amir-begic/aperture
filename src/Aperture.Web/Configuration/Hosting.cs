namespace Aperture.Web.Configuration;

/// <summary>
/// How this installation is reached. <c>Aperture:LocalOnly</c> is set by deploy/macos/install-laptop.sh: Aperture then
/// listens on this computer only, so there's nothing for a phone to reach. The login is created in the browser on
/// first start (see LoginStore), and the phone features (calendar feed, notifications) are off.
/// </summary>
public sealed record Hosting(bool LocalOnly)
{
    public static Hosting From(IConfiguration configuration) => new(configuration.GetValue<bool>("Aperture:LocalOnly"));
}
