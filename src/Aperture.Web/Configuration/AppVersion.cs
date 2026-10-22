using System.Reflection;

namespace Aperture.Web.Configuration;

public static class AppVersion
{
    /// <summary>Set by deploy/publish.sh (e.g. "2026.10.20-58-ed1231e"); "dev" for local builds.</summary>
    public static string Current { get; } =
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            .Split('+')[0] is { Length: > 0 } version && version != "1.0.0"
            ? version
            : "dev";
}
