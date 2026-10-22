using Microsoft.Data.Sqlite;

namespace Aperture.Web.Configuration;

/// <summary>
/// Where the app keeps its data. Configured via <c>Aperture:DataDirectory</c>;
/// kept outside the app folder in production so deployments never touch it.
/// </summary>
public sealed record AperturePaths(string DataDirectory)
{
    public string DatabasePath => Path.Combine(DataDirectory, "aperture.db");

    public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");

    /// <summary>Keys that sign login cookies; kept with the data so updates don't log you out.</summary>
    public string KeysDirectory => Path.Combine(DataDirectory, "keys");

    /// <summary>The key pair that signs notifications (VAPID); the devices' subscriptions are tied to it.</summary>
    public string PushKeysFile => Path.Combine(KeysDirectory, "push-vapid.json");

    /// <summary>Written by the nightly backup script (deploy/macos/backup.sh).</summary>
    public string BackupStatusFile => Path.Combine(DataDirectory, "backup-status.json");

    /// <summary>Downloaded releases and the hand-over to the updater (deploy/macos/auto-update.sh).</summary>
    public string UpdatesDirectory => Path.Combine(DataDirectory, "updates");

    /// <summary>Holds the file name of a downloaded release; writing it starts the updater (launchd WatchPaths).</summary>
    public string UpdateRequestFile => Path.Combine(UpdatesDirectory, "request");

    /// <summary>Written by the updater: running, succeeded or failed.</summary>
    public string UpdateStatusFile => Path.Combine(UpdatesDirectory, "status.json");

    public string ConnectionString =>
        new SqliteConnectionStringBuilder { DataSource = DatabasePath }.ToString();

    public static AperturePaths From(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration["Aperture:DataDirectory"] ?? "data";

        // Allow "~/aperture-data" in the Mac mini config.
        if (configured.StartsWith('~'))
        {
            configured = Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), configured[1..]);
        }

        return new AperturePaths(Path.GetFullPath(configured, environment.ContentRootPath));
    }
}
