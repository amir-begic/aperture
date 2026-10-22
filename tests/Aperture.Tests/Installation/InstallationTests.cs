using System.Formats.Tar;
using System.IO.Compression;
using System.Xml.Linq;
using Aperture.Web.Features.Updates;
using Aperture.Web.Installation;

namespace Aperture.Tests.Installation;

public sealed class InstallationTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("aperture-home-").FullName;
    private ApertureHome Home => new(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>Remembers the system tools that would run.</summary>
    private sealed class RecordingRunner : ICommandRunner
    {
        public List<string> Commands { get; } = [];

        public Task<CommandResult> RunAsync(string file, params string[] args)
        {
            Commands.Add(string.Join(' ', [file, .. args]));
            return Task.FromResult(new CommandResult(0, ""));
        }
    }

    private Updater Updater(Platform? platform = null) =>
        new(Home, platform ?? new LinuxPlatform(new RecordingRunner()), new FixedTimeProvider(new DateTime(2026, 10, 23)), new CommandLog(null));

    /// <summary>A release package as deploy/publish.sh makes it: one folder holding the program.</summary>
    private string Package(string folder = "aperture-2026.10.23-30-abc1234", params string[] files)
    {
        var source = Path.Combine(_root, "package-source");
        foreach (var file in files.Length > 0 ? files : [$"{folder}/Aperture.Web", $"{folder}/wwwroot/app.css"])
        {
            var path = Path.Combine(source, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, file);
        }

        var package = Path.Combine(_root, $"{folder}-{Platform.RuntimeIdentifier}.tar.gz");
        using (var output = File.Create(package))
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
        {
            TarFile.CreateFromDirectory(source, gzip, includeBaseDirectory: false);
        }
        Directory.Delete(source, recursive: true);
        return package;
    }

    [Fact]
    public void Settings_round_trip_through_aperture_json()
    {
        new HomeSettings(5191, "https://example.test/latest.json").Write(Home);

        Assert.Equal(new HomeSettings(5191, "https://example.test/latest.json"), HomeSettings.Read(Home));
        Assert.Null(HomeSettings.Read(new ApertureHome(Path.Combine(_root, "nothing-here"))));
    }

    [Fact]
    public void A_first_installation_takes_the_first_free_port()
    {
        Assert.Equal(5090, Ports.Choose(null, null, new CommandLog(null), isFree: _ => true));
        Assert.Equal(5092, Ports.Choose(null, null, new CommandLog(null), isFree: p => p > 5091));
        Assert.Throws<InstallationException>(() => Ports.Choose(null, null, new CommandLog(null), isFree: _ => false));
    }

    [Fact]
    public void An_installation_keeps_its_port_or_the_one_asked_for_and_never_moves_on()
    {
        var free = (int p) => p != 5091 && p != 6000;

        Assert.Equal(6001, Ports.Choose(6001, 5091, new CommandLog(null), free));
        Assert.Equal(5095, Ports.Choose(null, 5095, new CommandLog(null), free));
        Assert.Throws<InstallationException>(() => Ports.Choose(null, 5091, new CommandLog(null), free));
        Assert.Throws<InstallationException>(() => Ports.Choose(6000, null, new CommandLog(null), free));
        Assert.Throws<InstallationException>(() => Ports.Choose(70000, null, new CommandLog(null), _ => true));
    }

    [Fact]
    public void A_port_another_program_listens_on_isnt_free()
    {
        var other = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        other.Start();
        var port = ((System.Net.IPEndPoint)other.LocalEndpoint).Port;
        try
        {
            Assert.False(Ports.IsFree(port));
        }
        finally
        {
            other.Stop();
        }

        Assert.True(Ports.IsFree(port));
    }

    [Fact]
    public void Home_comes_from_the_argument_first()
    {
        Assert.Equal(Path.GetFullPath(_root), ApertureHome.Resolve(_root).Root);
        Assert.Equal("/x", ApertureHome.FromArguments(["backup", "--home", "/x"]));
        Assert.Null(ApertureHome.FromArguments(["backup"]));
    }

    [Fact]
    public void Unpacks_a_release_into_releases()
    {
        var release = Updater().Unpack(Package(), current: null);

        Assert.Equal(Path.Combine(Home.Releases, "aperture-2026.10.23-30-abc1234"), release);
        Assert.True(File.Exists(Path.Combine(release, "wwwroot", "app.css")));
    }

    [Fact]
    public void Refuses_a_package_with_more_than_one_folder()
    {
        var package = Package("aperture-x", "aperture-x/Aperture.Web", "elsewhere/evil.sh");

        Assert.Throws<InstallationException>(() => Updater().Unpack(package, current: null));
        Assert.False(Directory.Exists(Path.Combine(_root, "elsewhere")));
    }

    [Fact]
    public void Refuses_a_package_without_the_program_for_this_system()
    {
        var package = Package("aperture-x", "aperture-x/readme.txt");

        Assert.Throws<InstallationException>(() => Updater().Unpack(package, current: null));
        Assert.False(Directory.Exists(Path.Combine(Home.Releases, "aperture-x")));
    }

    [Fact]
    public async Task Without_a_request_the_updater_has_nothing_to_do()
    {
        Assert.Equal(0, await Updater().RunAsync(package: null));
    }

    [Fact]
    public async Task A_request_for_a_file_that_isnt_there_is_refused()
    {
        var updates = Path.Combine(Home.Data, "updates");
        Directory.CreateDirectory(updates);
        File.WriteAllText(Path.Combine(updates, "request"), "../../../etc/passwd\n");

        Assert.Equal(1, await Updater().RunAsync(package: null));

        var status = UpdateStatus.Read(new Aperture.Web.Configuration.AperturePaths(Home.Data))!;
        Assert.True(status.Failed);
        Assert.False(File.Exists(Path.Combine(updates, "request")));
    }

    [Fact]
    public void Mac_agents_are_valid_property_lists()
    {
        var agents = MacPlatform.Agents(Home).ToDictionary(a => a.Label, a => XDocument.Parse(a.Plist));

        Assert.Equal([MacPlatform.WebLabel, MacPlatform.UpdaterLabel, MacPlatform.BackupLabel], agents.Keys);
        var updater = agents[MacPlatform.UpdaterLabel].ToString();
        Assert.Contains(Path.Combine(Home.App, "Aperture.Web"), updater);
        Assert.Contains(Path.Combine(Home.Data, "updates", "request"), updater);
        // External drives need Full Disk Access, granted to /bin/bash once; the program's path changes with each release.
        Assert.Contains("<string>/bin/bash</string>", agents[MacPlatform.BackupLabel].ToString());
    }

    [Fact]
    public void Linux_units_run_the_program_from_app()
    {
        var units = LinuxPlatform.UnitFiles(Home).ToDictionary(u => u.Name, u => u.Unit);

        Assert.Equal(LinuxPlatform.Units, units.Keys);
        Assert.Contains($"ExecStart={Path.Combine(Home.App, "Aperture.Web")}\n", units[LinuxPlatform.WebUnit]);
        Assert.Contains($"ExecStart={Path.Combine(Home.App, "Aperture.Web")} update\n", units["aperture-updater.service"]);
        Assert.Contains($"PathExists={Path.Combine(Home.Data, "updates", "request")}", units["aperture-updater.path"]);
        Assert.Contains("OnCalendar=*-*-* 13:00:00", units["aperture-backup.timer"]);
    }

    [Fact]
    public void Windows_tasks_are_valid_and_quote_the_home_folder()
    {
        var home = new ApertureHome(@"C:\Users\Anna Rossi\AppData\Local\Aperture");
        XNamespace task = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        var tasks = WindowsPlatform.Tasks(home, @"PC\Anna Rossi", new DateTime(2026, 10, 23))
            .ToDictionary(t => t.Name, t => XDocument.Parse(t.Xml));

        Assert.Equal([WindowsPlatform.WebTask, WindowsPlatform.BackupTask, WindowsPlatform.UpdaterTask], tasks.Keys);
        var backup = tasks[WindowsPlatform.BackupTask];
        Assert.Equal(@"backup --home ""C:\Users\Anna Rossi\AppData\Local\Aperture""", backup.Descendants(task + "Arguments").Single().Value);
        Assert.Equal("2026-10-23T13:00:00", backup.Descendants(task + "StartBoundary").Single().Value);
        Assert.Single(tasks[WindowsPlatform.WebTask].Descendants(task + "LogonTrigger"));
        Assert.Empty(tasks[WindowsPlatform.UpdaterTask].Descendants(task + "Triggers").Single().Elements()); // on demand only
    }

    [Theory]
    [InlineData("/home/anna/Aperture", true)]
    [InlineData("/home/anna/My Aperture", false)]
    [InlineData("/home/anna/$HOME", false)]
    public void Unix_homes_must_be_simple_paths(string root, bool fine)
    {
        var check = () => new LinuxPlatform(new RecordingRunner()).CheckHome(new ApertureHome(root));
        if (fine)
        {
            check();
        }
        else
        {
            Assert.Throws<InstallationException>(check);
        }
    }

    /// <summary>A platform with no services to start or stop, for testing what the installer does with files.</summary>
    private sealed class FilesOnlyPlatform() : Platform(new RecordingRunner())
    {
        public override string ThisComputer => "this computer";
        public override Task RegisterAsync(ApertureHome home) => Task.CompletedTask;
        public override Task StartBackupsAsync(ApertureHome home) => Task.CompletedTask;
        public override Task UnregisterAsync(ApertureHome home) => Task.CompletedTask;
        public override Task StartWebAsync(ApertureHome home) => Task.CompletedTask;
        protected override Task StopWebServiceAsync(ApertureHome home) => Task.CompletedTask;
    }

    /// <summary>An installation as the install command leaves it, with data, plus a file Aperture didn't make.</summary>
    private void Installed()
    {
        var release = Path.Combine(Home.Releases, "aperture-2026.10.23-30-abc1234");
        Directory.CreateDirectory(release);
        Directory.CreateSymbolicLink(Home.App, release);
        Directory.CreateDirectory(Path.Combine(Home.Data, "attachments"));
        File.WriteAllText(Path.Combine(Home.Data, "aperture.db"), "");
        Directory.CreateDirectory(Path.Combine(Home.Backups, BackupJob.FolderName));
        Directory.CreateDirectory(Home.Logs);
        new HomeSettings(5090, null).Write(Home);
        BackupJob.WriteDefaultConfig(Home);
        File.WriteAllText(Path.Combine(_root, "my-notes.txt"), "not Aperture's");
    }

    private Installer Installer(ApertureHome? home = null) => new(home ?? Home, new FilesOnlyPlatform(), new CommandLog(null));

    [Fact]
    public async Task Uninstalling_keeps_the_data()
    {
        Installed();

        Assert.Equal(0, await Installer().UninstallAsync());

        Assert.Equal(["backup.conf", "backups", "data", "logs", "my-notes.txt"],
            Directory.GetFileSystemEntries(_root).Select(Path.GetFileName).Order());
    }

    [Fact]
    public async Task Removing_the_data_deletes_only_what_Aperture_made()
    {
        Installed();

        Assert.Equal(0, await Installer().UninstallAsync(removeData: true));

        Assert.Equal(["my-notes.txt"], Directory.GetFileSystemEntries(_root).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Removing_the_data_of_a_folder_that_isnt_an_installation_is_refused()
    {
        var folder = Path.Combine(_root, "somewhere");
        Directory.CreateDirectory(Path.Combine(folder, "data"));
        File.WriteAllText(Path.Combine(folder, "data", "important.txt"), "");

        await Assert.ThrowsAsync<InstallationException>(() => Installer(new ApertureHome(folder)).UninstallAsync(removeData: true));

        Assert.True(File.Exists(Path.Combine(folder, "data", "important.txt")));
    }
}
