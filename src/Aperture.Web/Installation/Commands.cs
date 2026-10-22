namespace Aperture.Web.Installation;

/// <summary>
/// The program's commands besides running the web app. They're the same on every system; Platform covers the
/// differences.
/// <code>
/// Aperture.Web install [--feed &lt;latest.json URL&gt;] [--port &lt;port&gt;] [--no-open]
/// Aperture.Web update [&lt;aperture-…-&lt;runtime&gt;.tar.gz&gt;]   (without one: the update Aperture downloaded)
/// Aperture.Web backup
/// Aperture.Web restore &lt;backup ZIP, its folder, or a daily aperture-YYYY-MM-DD.db&gt;
/// Aperture.Web uninstall
/// </code>
/// Each takes --home &lt;folder&gt; for an installation elsewhere than the usual place (ApertureHome).
/// </summary>
public static class Commands
{
    public static readonly IReadOnlySet<string> Names = new HashSet<string> { "install", "update", "backup", "restore", "uninstall" };

    /// <summary>The exit code, or null when <paramref name="args"/> isn't a command (then the web app starts).</summary>
    public static async Task<int?> TryRunAsync(string[] args)
    {
        if (args.Length == 0 || !Names.Contains(args[0]))
        {
            return null;
        }

        var command = args[0];
        var home = ApertureHome.Resolve(ApertureHome.FromArguments(args));
        var platform = Platform.Current();
        var log = CommandLog.For(home, command);
        var installer = new Installer(home, platform, log);
        try
        {
            return command switch
            {
                "install" => await installer.InstallAsync(
                    Option(args, "--feed"),
                    Option(args, "--port") is { } port ? int.Parse(port) : null,
                    openBrowser: !args.Contains("--no-open") && Environment.GetEnvironmentVariable("APERTURE_NO_OPEN") != "1"),
                "update" => await new Updater(home, platform, TimeProvider.System, log).RunAsync(Positional(args)),
                "backup" => new BackupJob(home, TimeProvider.System, log).Run(),
                "restore" => Positional(args) is { } source
                    ? await installer.RestoreAsync(source)
                    : Usage(log, "restore <backup ZIP, its folder, or a daily aperture-YYYY-MM-DD.db>"),
                "uninstall" => await installer.UninstallAsync(),
                _ => null,
            };
        }
        catch (Exception e) when (e is InstallationException or IOException or UnauthorizedAccessException or FormatException)
        {
            log.Write($"Error: {e.Message}");
            return 1;
        }
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>The first argument after the command that isn't an option or an option's value.</summary>
    private static string? Positional(string[] args)
    {
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i].StartsWith("--"))
            {
                i += args[i] is "--no-open" ? 0 : 1;
                continue;
            }

            return args[i];
        }

        return null;
    }

    private static int Usage(CommandLog log, string usage)
    {
        log.Write($"Usage: Aperture.Web {usage}");
        return 2;
    }
}
