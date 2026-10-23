using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aperture.Web.Installation;

/// <summary>
/// Where an installed Aperture lives: the program (releases/, and app/ pointing at the current one), the data, logs,
/// backups and its settings (aperture.json, written by the install command). macOS: ~/Aperture; Windows:
/// %LOCALAPPDATA%\Aperture; Linux: ~/.local/share/aperture. --home or APERTURE_HOME choose another.
/// </summary>
public sealed record ApertureHome(string Root)
{
    /// <summary>
    /// Where a first installation starts looking for a free port (see Ports). Below the ports the systems hand out for
    /// outgoing connections (from 32768 on Linux, 49152 on macOS and Windows), and away from well-known ones, including
    /// the 5000s of .NET development servers: a development copy runs on 5090 (launchSettings.json).
    /// </summary>
    public const int DefaultPort = 29873;

    public string App => Path.Combine(Root, "app");
    public string Releases => Path.Combine(Root, "releases");
    public string Data => Path.Combine(Root, "data");
    public string Logs => Path.Combine(Root, "logs");
    public string Backups => Path.Combine(Root, "backups");
    public string BeforeUpdateBackups => Path.Combine(Backups, "before-update");

    /// <summary>The installed copy's configuration: port, data folder, update feed.</summary>
    public string SettingsFile => Path.Combine(Root, "aperture.json");

    public string Log(string name) => Path.Combine(Logs, $"{name}.log");

    /// <summary><paramref name="home"/> (--home), else APERTURE_HOME, else this system's usual place.</summary>
    public static ApertureHome Resolve(string? home = null) => new(Path.GetFullPath(
        !string.IsNullOrWhiteSpace(home) ? home
        : Environment.GetEnvironmentVariable("APERTURE_HOME") is { Length: > 0 } fromEnvironment ? fromEnvironment
        : DefaultRoot()));

    /// <summary>The value after <c>--home</c> in command-line arguments, if any.</summary>
    public static string? FromArguments(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i] == "--home")
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static string DefaultRoot()
    {
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aperture");
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(user, "Aperture");
        }

        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return Path.Combine(string.IsNullOrWhiteSpace(dataHome) ? Path.Combine(user, ".local", "share") : dataHome, "aperture");
    }
}

/// <summary>What aperture.json holds. The web app reads it as configuration; the commands read and write it here.</summary>
public sealed record HomeSettings(int Port, string? FeedUrl)
{
    public string Url => $"http://localhost:{Port}";

    public static HomeSettings? Read(ApertureHome home)
    {
        try
        {
            var json = JsonNode.Parse(File.ReadAllText(home.SettingsFile));
            var urls = json?["Urls"]?.GetValue<string>() ?? "";
            var port = Uri.TryCreate(urls.Split(';')[0], UriKind.Absolute, out var uri) ? uri.Port : ApertureHome.DefaultPort;
            return new HomeSettings(port, json?["Updates"]?["FeedUrl"]?.GetValue<string>());
        }
        catch (Exception e) when (e is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes aperture.json. On Windows Aperture runs without a console (see the project file), so it logs to a file
    /// (Aperture:LogFile); launchd and systemd catch the console output themselves.
    /// </summary>
    public void Write(ApertureHome home)
    {
        var json = new JsonObject
        {
            ["Urls"] = Url,
            ["Aperture"] = new JsonObject
            {
                ["DataDirectory"] = home.Data,
                ["LogFile"] = OperatingSystem.IsWindows() ? home.Log("aperture") : null,
            },
            ["Updates"] = new JsonObject { ["FeedUrl"] = FeedUrl },
        };
        Directory.CreateDirectory(home.Root);
        var temp = home.SettingsFile + ".tmp";
        File.WriteAllText(temp, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        File.Move(temp, home.SettingsFile, overwrite: true);
    }
}
