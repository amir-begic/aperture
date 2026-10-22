using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace Aperture.Web.Installation;

/// <summary>Something the system refused while installing or updating; the message says what.</summary>
public sealed class InstallationException(string message) : Exception(message);

public sealed record CommandResult(int ExitCode, string Output)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>Runs system tools (launchctl, systemctl, schtasks, …). Tests replace it to see what would run.</summary>
public interface ICommandRunner
{
    Task<CommandResult> RunAsync(string file, params string[] args);
}

public sealed class ProcessRunner : ICommandRunner
{
    public async Task<CommandResult> RunAsync(string file, params string[] args)
    {
        var start = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return new CommandResult(process.ExitCode, (await output + await error).Trim());
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            return new CommandResult(-1, $"{file}: {e.Message}");
        }
    }
}

/// <summary>
/// What differs between macOS, Windows and Linux: how Aperture starts at login, how the daily backup and the updater are
/// scheduled, and how app/ points at the current release. Everything else (install, update, backup) is the same code.
/// </summary>
public abstract class Platform(ICommandRunner run)
{
    protected ICommandRunner Run { get; } = run;

    public static Platform Current(ICommandRunner? runner = null)
    {
        runner ??= new ProcessRunner();
        return OperatingSystem.IsMacOS() ? new MacPlatform(runner)
            : OperatingSystem.IsWindows() ? new WindowsPlatform(runner)
            : new LinuxPlatform(runner);
    }

    /// <summary>
    /// The runtime the releases are built for, e.g. "osx-arm64"; part of each package's name. The program's own
    /// architecture, not the computer's: Windows on Arm runs the x64 build, and updates to the x64 build.
    /// </summary>
    public static string RuntimeIdentifier =>
        (OperatingSystem.IsMacOS() ? "osx" : OperatingSystem.IsWindows() ? "win" : "linux") + "-"
        + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

    /// <summary>"this Mac", "this PC", "this computer": for texts in the app.</summary>
    public abstract string ThisComputer { get; }

    public virtual string ExecutableName => "Aperture.Web";

    public string Executable(string releaseDirectory) => Path.Combine(releaseDirectory, ExecutableName);

    /// <summary>Writes what starts Aperture at login, the daily backup and the updater, and loads the updater.</summary>
    public abstract Task RegisterAsync(ApertureHome home);

    /// <summary>
    /// Starts the daily backup's schedule and runs one now. Only once Aperture is up: before, there's no database yet.
    /// </summary>
    public abstract Task StartBackupsAsync(ApertureHome home);

    public abstract Task UnregisterAsync(ApertureHome home);

    public abstract Task StartWebAsync(ApertureHome home);

    /// <summary>Stops Aperture and waits until it has closed the database.</summary>
    public async Task StopWebAsync(ApertureHome home)
    {
        await StopWebServiceAsync(home);
        await WaitForExitAsync(home);
    }

    protected abstract Task StopWebServiceAsync(ApertureHome home);

    /// <summary>Starts the updater after Aperture asked for an update. macOS and Linux watch the request file instead.</summary>
    public virtual Task StartUpdaterAsync(ApertureHome home) => Task.CompletedTask;

    /// <summary>Anything a freshly unpacked release needs before it can run.</summary>
    public virtual Task PrepareReleaseAsync(string releaseDirectory) => Task.CompletedTask;

    /// <summary>Points app/ at <paramref name="releaseDirectory"/>; the service definitions run app/.</summary>
    public virtual Task LinkAppAsync(ApertureHome home, string releaseDirectory)
    {
        RemoveAppLink(home);
        Directory.CreateSymbolicLink(home.App, releaseDirectory);
        return Task.CompletedTask;
    }

    /// <summary>The release app/ points at, or null.</summary>
    public static string? CurrentRelease(ApertureHome home)
    {
        var app = new DirectoryInfo(home.App);
        return app.Exists && app.LinkTarget is { } target ? Path.GetFullPath(target, home.Root) : null;
    }

    /// <summary>
    /// Creates what opens Aperture in its own window: a Start menu entry (with <paramref name="desktopShortcut"/>, also
    /// one on the desktop), an app in Applications, or an entry in the applications menu. The window is a Chromium
    /// browser's app mode (no tabs, no address bar); without such a browser, Aperture opens in the default one.
    /// </summary>
    public abstract Task CreateLauncherAsync(ApertureHome home, int port, bool desktopShortcut);

    /// <summary>Opens Aperture in its own window, as the launcher does.</summary>
    public abstract Task OpenAppAsync(ApertureHome home, int port);

    /// <summary>The argument that opens Aperture in a Chromium browser's app mode.</summary>
    public static string AppArgument(int port) => $"--app=http://localhost:{port}";

    /// <summary>Starts a program without waiting for it: a browser window stays open long after the install command ends.</summary>
    protected static void Start(string file, params string[] args)
    {
        var start = new ProcessStartInfo(file) { UseShellExecute = false };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }
        Process.Start(start)?.Dispose();
    }

    /// <summary>Opens a web address in the default browser.</summary>
    protected static void OpenInDefaultBrowser(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();

    /// <summary>Removes app/ if it's a link. A real folder there is left alone: it isn't ours to delete.</summary>
    protected static void RemoveAppLink(ApertureHome home)
    {
        var app = new DirectoryInfo(home.App);
        if (app.LinkTarget is not null)
        {
            app.Delete();
        }
        else if (app.Exists)
        {
            throw new InstallationException($"{home.App} is a folder, not a link to a release. Move it away and try again.");
        }
    }

    /// <summary>Waits for Aperture processes from this home to end (half a minute), then ends them.</summary>
    private static async Task WaitForExitAsync(ApertureHome home)
    {
        for (var i = 0; i < 60; i++)
        {
            if (WebProcesses(home).Count == 0)
            {
                return;
            }
            await Task.Delay(500);
        }

        foreach (var process in WebProcesses(home))
        {
            try
            {
                process.Kill();
                await process.WaitForExitAsync();
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Gone already.
            }
        }
    }

    /// <summary>Aperture web processes started from this home, except this one (the updater runs the same program).</summary>
    private static List<Process> WebProcesses(ApertureHome home)
    {
        var self = Environment.ProcessId;
        var result = new List<Process>();
        foreach (var process in Process.GetProcessesByName("Aperture.Web"))
        {
            try
            {
                if (process.Id != self && process.MainModule?.FileName is { } file
                    && Path.GetFullPath(file).StartsWith(home.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && !IsCommand(process))
                {
                    result.Add(process);
                }
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Not ours to look at, or gone.
            }
        }

        return result;
    }

    /// <summary>Another command (backup, update) runs the same program; only the web app is stopped.</summary>
    private static bool IsCommand(Process process)
    {
        if (OperatingSystem.IsLinux() && File.Exists($"/proc/{process.Id}/cmdline"))
        {
            var args = File.ReadAllText($"/proc/{process.Id}/cmdline").Split('\0');
            return args.Length > 1 && Commands.Names.Contains(args[1]);
        }

        // Elsewhere the command line isn't readable without extra permissions; the web app is what runs long.
        return false;
    }

    protected async Task RequireAsync(string what, string file, params string[] args)
    {
        var result = await Run.RunAsync(file, args);
        if (!result.Succeeded)
        {
            throw new InstallationException($"{what} failed ({file} {string.Join(' ', args)}): {result.Output}");
        }
    }

    protected static string Xml(string text) => SecurityElement.Escape(text);

    /// <summary>Install folders go into service definitions unquoted (launchd, systemd); keep them simple.</summary>
    public virtual void CheckHome(ApertureHome home)
    {
        if (home.Root.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'' or '%' or '$' or '\\'))
        {
            throw new InstallationException($"The install folder can't contain spaces, quotes, % or $: {home.Root}");
        }
    }
}

/// <summary>launchd agents of the person logged in (~/Library/LaunchAgents), like before.</summary>
public sealed class MacPlatform(ICommandRunner run) : Platform(run)
{
    public const string WebLabel = "ch.aperture.web";
    public const string UpdaterLabel = "ch.aperture.updater";
    public const string BackupLabel = "ch.aperture.backup";

    public override string ThisComputer => "this Mac";

    private static string AgentsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents");

    private static string Agent(string label) => Path.Combine(AgentsDirectory, label + ".plist");

    private static string Domain => $"gui/{Native.getuid()}";

    public override async Task RegisterAsync(ApertureHome home)
    {
        Directory.CreateDirectory(AgentsDirectory);
        foreach (var (label, plist) in Agents(home))
        {
            File.WriteAllText(Agent(label), plist);
        }

        await Run.RunAsync("launchctl", "bootout", $"{Domain}/{UpdaterLabel}");
        await RequireAsync("Starting the updater", "launchctl", "bootstrap", Domain, Agent(UpdaterLabel));
    }

    public override async Task StartBackupsAsync(ApertureHome home)
    {
        await Run.RunAsync("launchctl", "bootout", $"{Domain}/{BackupLabel}");
        await RequireAsync("Starting the backup", "launchctl", "bootstrap", Domain, Agent(BackupLabel)); // runs at once (RunAtLoad)
    }

    public override async Task UnregisterAsync(ApertureHome home)
    {
        foreach (var label in new[] { WebLabel, UpdaterLabel, BackupLabel })
        {
            await Run.RunAsync("launchctl", "bootout", $"{Domain}/{label}");
            File.Delete(Agent(label));
        }

        if (Directory.Exists(LauncherApp))
        {
            Directory.Delete(LauncherApp, recursive: true);
        }
    }

    public override async Task StartWebAsync(ApertureHome home)
    {
        if (!(await Run.RunAsync("launchctl", "bootstrap", Domain, Agent(WebLabel))).Succeeded)
        {
            await RequireAsync("Starting Aperture", "launchctl", "kickstart", "-k", $"{Domain}/{WebLabel}");
        }
    }

    protected override Task StopWebServiceAsync(ApertureHome home) => Run.RunAsync("launchctl", "bootout", $"{Domain}/{WebLabel}");

    /// <summary>
    /// Downloads by Aperture carry no quarantine flag, but releases unpacked from a browser download do. Apple silicon
    /// only runs signed code: a release built on Linux gets signed here, ad hoc, if it isn't already.
    /// </summary>
    public override async Task PrepareReleaseAsync(string releaseDirectory)
    {
        await Run.RunAsync("xattr", "-dr", "com.apple.quarantine", releaseDirectory);
        var executable = Executable(releaseDirectory);
        if (!(await Run.RunAsync("codesign", "--verify", executable)).Succeeded)
        {
            await RequireAsync("Signing Aperture", "codesign", "--force", "--sign", "-", executable);
        }
    }

    /// <summary>~/Applications/Aperture.app: in Launchpad and Spotlight, and can be kept in the Dock.</summary>
    private static string LauncherApp =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications", "Aperture.app");

    public override async Task CreateLauncherAsync(ApertureHome home, int port, bool desktopShortcut)
    {
        if (Directory.Exists(LauncherApp))
        {
            Directory.Delete(LauncherApp, recursive: true);
        }

        var contents = Path.Combine(LauncherApp, "Contents");
        Directory.CreateDirectory(Path.Combine(contents, "MacOS"));
        Directory.CreateDirectory(Path.Combine(contents, "Resources"));
        File.WriteAllText(Path.Combine(contents, "Info.plist"), LauncherInfoPlist);
        var script = Path.Combine(contents, "MacOS", "Aperture");
        File.WriteAllText(script, LauncherScript(port));
        if (!OperatingSystem.IsWindows()) // always so here; the analyzer can't know
        {
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
        var icon = Path.Combine(home.App, "deploy", "macos", "Aperture.icns");
        if (File.Exists(icon))
        {
            File.Copy(icon, Path.Combine(contents, "Resources", "Aperture.icns"), overwrite: true);
        }

        // So Launchpad and the Dock know it, with its icon, right away.
        await Run.RunAsync(
            "/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister", "-f", LauncherApp);
    }

    public override Task OpenAppAsync(ApertureHome home, int port) =>
        Run.RunAsync("open", Directory.Exists(LauncherApp) ? LauncherApp : $"http://localhost:{port}");

    /// <summary>
    /// The launcher: a script, so it needs no signing. Safari has no app mode, so it looks for a Chromium browser
    /// first; Safari users can still use File → Add to Dock.
    /// </summary>
    public static string LauncherScript(int port) => $$"""
        #!/bin/bash
        # Opens Aperture in its own window: a Chromium browser's app mode (no tabs, no address bar), or the default
        # browser if there's none. Written by "Aperture.Web install".
        url="http://localhost:{{port}}"
        for browser in "Google Chrome" "Microsoft Edge" "Brave Browser" "Chromium"; do
            for folder in /Applications "$HOME/Applications"; do
                if [[ -d "$folder/$browser.app" ]]; then
                    # -n: a new instance, so a running browser still gets the --app argument (it hands it over).
                    exec open -na "$folder/$browser.app" --args --app="$url"
                fi
            done
        done
        exec open "$url"

        """;

    /// <summary>LSUIElement: the launcher itself never shows in the Dock, only Aperture's window does.</summary>
    public const string LauncherInfoPlist = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
            <key>CFBundleName</key>
            <string>Aperture</string>
            <key>CFBundleDisplayName</key>
            <string>Aperture</string>
            <key>CFBundleIdentifier</key>
            <string>ch.aperture.window</string>
            <key>CFBundleExecutable</key>
            <string>Aperture</string>
            <key>CFBundleIconFile</key>
            <string>Aperture</string>
            <key>CFBundlePackageType</key>
            <string>APPL</string>
            <key>CFBundleShortVersionString</key>
            <string>1.0</string>
            <key>LSUIElement</key>
            <true/>
        </dict>
        </plist>

        """;

    /// <summary>
    /// The three agents. The backup runs through /bin/bash (deploy/macos/backup.sh): Full Disk Access for external
    /// drives is granted to /bin/bash once, while the program's path changes with every release.
    /// </summary>
    public static IEnumerable<(string Label, string Plist)> Agents(ApertureHome home)
    {
        var app = home.App;
        var environment = $"""
                <key>EnvironmentVariables</key>
                <dict>
                    <key>APERTURE_HOME</key>
                    <string>{Xml(home.Root)}</string>
                    <key>ASPNETCORE_ENVIRONMENT</key>
                    <string>Production</string>
                    <key>DOTNET_gcServer</key>
                    <string>0</string>
                </dict>
            """;
        string Log(string name) => $"""
                <key>StandardOutPath</key>
                <string>{Xml(home.Log(name))}</string>
                <key>StandardErrorPath</key>
                <string>{Xml(home.Log(name))}</string>
            """;
        string Plist(string comment, string label, string body) => $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <!-- {comment} Written by "Aperture.Web install". -->
            <plist version="1.0">
            <dict>
                <key>Label</key>
                <string>{label}</string>
            {body}
            </dict>
            </plist>

            """;

        yield return (WebLabel, Plist("Aperture: runs while you're logged in, restarts if it stops.", WebLabel, $"""
                <key>ProgramArguments</key>
                <array>
                    <string>{Xml(Path.Combine(app, "Aperture.Web"))}</string>
                </array>
                <key>WorkingDirectory</key>
                <string>{Xml(app)}</string>
            {environment}
                <key>RunAtLoad</key>
                <true/>
                <key>KeepAlive</key>
                <true/>
                <key>ThrottleInterval</key>
                <integer>10</integer>
            {Log("aperture")}
            """));

        yield return (UpdaterLabel, Plist("Installs an update Aperture downloaded, as soon as it asks for it.", UpdaterLabel, $"""
                <key>ProgramArguments</key>
                <array>
                    <string>{Xml(Path.Combine(app, "Aperture.Web"))}</string>
                    <string>update</string>
                </array>
            {environment}
                <key>WatchPaths</key>
                <array>
                    <string>{Xml(Path.Combine(home.Data, "updates", "request"))}</string>
                </array>
            {Log("update")}
            """));

        yield return (BackupLabel, Plist("The daily backup: at login and at 13:00, or on waking up if the Mac was asleep then.", BackupLabel, $"""
                <key>ProgramArguments</key>
                <array>
                    <string>/bin/bash</string>
                    <string>{Xml(Path.Combine(app, "deploy", "macos", "backup.sh"))}</string>
                </array>
            {environment}
                <key>RunAtLoad</key>
                <true/>
                <key>StartCalendarInterval</key>
                <dict>
                    <key>Hour</key>
                    <integer>13</integer>
                    <key>Minute</key>
                    <integer>0</integer>
                </dict>
            {Log("backup")}
            """));
    }
}

/// <summary>systemd units of the person logged in (~/.config/systemd/user): a service, a timer and a path unit.</summary>
public sealed class LinuxPlatform(ICommandRunner run) : Platform(run)
{
    public const string WebUnit = "aperture.service";
    public static readonly string[] Units =
        [WebUnit, "aperture-updater.service", "aperture-updater.path", "aperture-backup.service", "aperture-backup.timer"];

    public override string ThisComputer => "this computer";

    private static string UnitDirectory => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } config
            ? config
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
        "systemd", "user");

    public override async Task RegisterAsync(ApertureHome home)
    {
        Directory.CreateDirectory(UnitDirectory);
        foreach (var (name, unit) in UnitFiles(home))
        {
            File.WriteAllText(Path.Combine(UnitDirectory, name), unit);
        }

        await RequireAsync("Loading the services", "systemctl", "--user", "daemon-reload");
        await RequireAsync("Enabling Aperture", "systemctl", "--user", "enable", WebUnit);
        // The path unit starts the updater when Aperture asks for it.
        await RequireAsync("Enabling the updater", "systemctl", "--user", "enable", "--now", "aperture-updater.path");
    }

    /// <summary>Chromium browsers, which have an app mode, in the order they're preferred.</summary>
    private static readonly string[] Browsers =
        ["microsoft-edge", "microsoft-edge-stable", "google-chrome", "google-chrome-stable", "chromium", "chromium-browser", "brave-browser"];

    /// <summary>The first Chromium browser on the PATH, or null.</summary>
    private static string? Browser() =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(folder => Browsers.Select(name => Path.Combine(folder, name)))
            .FirstOrDefault(File.Exists);

    /// <summary>The entry in the applications menu. The browser is looked for when installing; install again after adding one.</summary>
    public override Task CreateLauncherAsync(ApertureHome home, int port, bool desktopShortcut)
    {
        Directory.CreateDirectory(ApplicationsDirectory);
        File.WriteAllText(DesktopEntry, DesktopEntryText(home, port, Browser()));
        return Task.CompletedTask;
    }

    public override Task OpenAppAsync(ApertureHome home, int port)
    {
        if (Browser() is { } browser)
        {
            Start(browser, AppArgument(port));
        }
        else
        {
            Start("xdg-open", $"http://localhost:{port}");
        }
        return Task.CompletedTask;
    }

    public static string DesktopEntryText(ApertureHome home, int port, string? browser) => $"""
        [Desktop Entry]
        Type=Application
        Name=Aperture
        Comment=Photo office: shoots, invoices and finances
        Exec={(browser is null ? $"xdg-open http://localhost:{port}" : $"\"{browser}\" {AppArgument(port)}")}
        Icon={Path.Combine(home.App, "wwwroot", "icon-512.png")}
        Categories=Office;
        Terminal=false

        """;

    public override async Task StartBackupsAsync(ApertureHome home)
    {
        // The timer runs the backup daily and two minutes after login.
        await RequireAsync("Enabling the backup", "systemctl", "--user", "enable", "--now", "aperture-backup.timer");
        await Run.RunAsync("systemctl", "--user", "start", "--no-block", "aperture-backup.service");
    }

    private static string ApplicationsDirectory => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } data
            ? data
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share"),
        "applications");

    private static string DesktopEntry => Path.Combine(ApplicationsDirectory, "aperture.desktop");

    public override async Task UnregisterAsync(ApertureHome home)
    {
        await Run.RunAsync("systemctl", ["--user", "disable", "--now", .. Units]);
        foreach (var name in Units)
        {
            File.Delete(Path.Combine(UnitDirectory, name));
        }
        File.Delete(DesktopEntry);
        await Run.RunAsync("systemctl", "--user", "daemon-reload");
    }

    public override Task StartWebAsync(ApertureHome home) =>
        RequireAsync("Starting Aperture", "systemctl", "--user", "restart", WebUnit);

    protected override Task StopWebServiceAsync(ApertureHome home) => Run.RunAsync("systemctl", "--user", "stop", WebUnit);

    public static IEnumerable<(string Name, string Unit)> UnitFiles(ApertureHome home)
    {
        var program = Path.Combine(home.App, "Aperture.Web");
        var environment = $"""
            Environment=APERTURE_HOME={home.Root}
            Environment=ASPNETCORE_ENVIRONMENT=Production
            Environment=DOTNET_gcServer=0
            """;
        string Header(string description) => $"""
            # {description} Written by "Aperture.Web install".
            [Unit]
            Description={description}
            """;

        yield return (WebUnit, $"""
            {Header("Aperture: runs while you're logged in, restarts if it stops.")}

            [Service]
            ExecStart={program}
            WorkingDirectory={home.App}
            {environment}
            Restart=always
            RestartSec=10
            StandardOutput=append:{home.Log("aperture")}
            StandardError=append:{home.Log("aperture")}

            [Install]
            WantedBy=default.target

            """);

        yield return ("aperture-updater.service", $"""
            {Header("Aperture updater: installs an update Aperture downloaded.")}

            [Service]
            Type=oneshot
            ExecStart={program} update
            {environment}
            StandardOutput=append:{home.Log("update")}
            StandardError=append:{home.Log("update")}

            """);

        yield return ("aperture-updater.path", $"""
            {Header("Aperture updater: starts as soon as Aperture asks for an update.")}

            [Path]
            PathExists={Path.Combine(home.Data, "updates", "request")}

            [Install]
            WantedBy=default.target

            """);

        yield return ("aperture-backup.service", $"""
            {Header("Aperture backup.")}

            [Service]
            Type=oneshot
            ExecStart={program} backup
            {environment}
            StandardOutput=append:{home.Log("backup")}
            StandardError=append:{home.Log("backup")}

            """);

        yield return ("aperture-backup.timer", $"""
            {Header("Aperture backup: daily at 13:00 (or when the computer is next on), and after login.")}

            [Timer]
            OnCalendar=*-*-* 13:00:00
            Persistent=true
            OnStartupSec=2min

            [Install]
            WantedBy=timers.target

            """);
    }
}

/// <summary>
/// Scheduled tasks of the person logged in: Aperture at logon, the daily backup and the updater (started on demand).
/// Creating them needs no administrator rights. app\ is a junction, which also needs none.
/// </summary>
public sealed class WindowsPlatform(ICommandRunner run) : Platform(run)
{
    public const string WebTask = "Aperture";
    public const string BackupTask = "Aperture Backup";
    public const string UpdaterTask = "Aperture Updater";

    public override string ThisComputer => "this PC";

    public override string ExecutableName => "Aperture.Web.exe";

    /// <summary>Paths with spaces are fine here: task definitions quote them.</summary>
    public override void CheckHome(ApertureHome home)
    {
        if (home.Root.Contains('"'))
        {
            throw new InstallationException($"The install folder can't contain quotes: {home.Root}");
        }
    }

    public override async Task RegisterAsync(ApertureHome home)
    {
        var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
        foreach (var (name, xml) in Tasks(home, user, DateTime.Today))
        {
            var file = Path.Combine(Path.GetTempPath(), $"aperture-task-{Guid.NewGuid():N}.xml");
            File.WriteAllText(file, xml, Encoding.Unicode); // schtasks wants UTF-16
            try
            {
                await RequireAsync($"Creating the task \"{name}\"", "schtasks", "/Create", "/TN", name, "/XML", file, "/F");
            }
            finally
            {
                File.Delete(file);
            }
        }

    }

    public override Task StartBackupsAsync(ApertureHome home) => Run.RunAsync("schtasks", "/Run", "/TN", BackupTask);

    public override async Task UnregisterAsync(ApertureHome home)
    {
        foreach (var name in new[] { WebTask, BackupTask, UpdaterTask })
        {
            await Run.RunAsync("schtasks", "/End", "/TN", name);
            await Run.RunAsync("schtasks", "/Delete", "/TN", name, "/F");
        }

        RemoveLaunchers();
    }

    public override Task StartWebAsync(ApertureHome home) => RequireAsync("Starting Aperture", "schtasks", "/Run", "/TN", WebTask);

    protected override Task StopWebServiceAsync(ApertureHome home) => Run.RunAsync("schtasks", "/End", "/TN", WebTask);

    public override Task StartUpdaterAsync(ApertureHome home) =>
        RequireAsync("Starting the updater", "schtasks", "/Run", "/TN", UpdaterTask);

    private static string StartMenuFolder => Environment.GetFolderPath(Environment.SpecialFolder.Programs);
    private static string DesktopFolder => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    /// <summary>Edge first: it's on every Windows 10 and 11. Chrome if Edge isn't there.</summary>
    private static string? Browser()
    {
        var places = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        };
        return new[] { @"Microsoft\Edge\Application\msedge.exe", @"Google\Chrome\Application\chrome.exe" }
            .SelectMany(browser => places.Select(place => Path.Combine(place, browser)))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>Start menu entry, and with <paramref name="desktopShortcut"/> one on the desktop, with Aperture's icon.</summary>
    public override async Task CreateLauncherAsync(ApertureHome home, int port, bool desktopShortcut)
    {
        RemoveLaunchers();
        var icon = Path.Combine(home.App, ExecutableName);
        var folders = desktopShortcut ? new[] { StartMenuFolder, DesktopFolder } : [StartMenuFolder];
        if (Browser() is not { } browser)
        {
            foreach (var folder in folders)
            {
                File.WriteAllText(Path.Combine(folder, "Aperture.url"),
                    $"[InternetShortcut]\r\nURL=http://localhost:{port}\r\nIconFile={icon}\r\nIconIndex=0\r\n");
            }
            return;
        }

        foreach (var folder in folders)
        {
            await RequireAsync("Creating the Aperture shortcut", "powershell.exe", "-NoProfile", "-NonInteractive", "-Command",
                ShortcutScript(Path.Combine(folder, "Aperture.lnk"), browser, AppArgument(port), icon));
        }
    }

    public override Task OpenAppAsync(ApertureHome home, int port)
    {
        if (Browser() is { } browser)
        {
            Start(browser, AppArgument(port));
        }
        else
        {
            OpenInDefaultBrowser($"http://localhost:{port}");
        }
        return Task.CompletedTask;
    }

    /// <summary>A shortcut through Windows' own shortcut object; PowerShell strings in single quotes, quotes doubled.</summary>
    public static string ShortcutScript(string shortcut, string target, string arguments, string icon)
    {
        static string Quote(string text) => "'" + text.Replace("'", "''") + "'";
        return $"$s = (New-Object -ComObject WScript.Shell).CreateShortcut({Quote(shortcut)}); "
            + $"$s.TargetPath = {Quote(target)}; $s.Arguments = {Quote(arguments)}; $s.IconLocation = {Quote(icon + ",0")}; "
            + "$s.Description = 'Aperture'; $s.Save()";
    }

    private static void RemoveLaunchers()
    {
        foreach (var folder in new[] { StartMenuFolder, DesktopFolder })
        {
            File.Delete(Path.Combine(folder, "Aperture.lnk"));
            File.Delete(Path.Combine(folder, "Aperture.url"));
        }
    }

    public override async Task LinkAppAsync(ApertureHome home, string releaseDirectory)
    {
        RemoveAppLink(home);
        await RequireAsync("Linking the current release", "cmd", "/c", "mklink", "/J", home.App, releaseDirectory);
    }

    public static IEnumerable<(string Name, string Xml)> Tasks(ApertureHome home, string user, DateTime today)
    {
        var program = Path.Combine(home.App, "Aperture.Web.exe");
        var homeArgument = $"--home \"{home.Root}\"";
        var logon = $"""
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{Xml(user)}</UserId>
            """;
        string Task(string description, string triggers, string arguments, string timeLimit, string restart) => $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>{Xml(description)} Written by "Aperture.Web install".</Description>
              </RegistrationInfo>
              <Triggers>
            {triggers}
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{Xml(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>LeastPrivilege</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>{timeLimit}</ExecutionTimeLimit>
                <Priority>5</Priority>
            {restart}
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{Xml(program)}</Command>
                  <Arguments>{Xml(arguments)}</Arguments>
                  <WorkingDirectory>{Xml(home.App)}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>

            """;

        yield return (WebTask, Task("Aperture: runs while you're logged in, restarts if it stops.",
            logon + "\n    </LogonTrigger>", homeArgument, "PT0S", """
                <RestartOnFailure>
                  <Interval>PT1M</Interval>
                  <Count>999</Count>
                </RestartOnFailure>
            """));

        yield return (BackupTask, Task("Aperture backup: daily at 13:00 (or when the PC is next on), and after logon.",
            logon + "\n      <Delay>PT2M</Delay>\n    </LogonTrigger>\n" + $"""
                <CalendarTrigger>
                  <StartBoundary>{today:yyyy-MM-dd}T13:00:00</StartBoundary>
                  <Enabled>true</Enabled>
                  <ScheduleByDay>
                    <DaysInterval>1</DaysInterval>
                  </ScheduleByDay>
                </CalendarTrigger>
            """, $"backup {homeArgument}", "PT2H", ""));

        yield return (UpdaterTask, Task("Aperture updater: installs an update Aperture downloaded. Started by Aperture.",
            "", $"update {homeArgument}", "PT1H", ""));
    }
}

internal static class Native
{
    // The user's id, for launchd's gui/<uid> domain.
    [DllImport("libc")]
    internal static extern uint getuid();
}
