using System.Text.Json;
using System.Text.Json.Serialization;
using Aperture.Web.Configuration;

namespace Aperture.Web.Features.Backup;

/// <summary>
/// The result of the last nightly backup, as written by deploy/macos/backup.sh. The app only reads it:
/// backups run outside the app, so they still happen when the app itself is broken.
/// </summary>
public sealed record BackupStatus(
    [property: JsonPropertyName("lastRun")] DateTimeOffset? LastRun,
    [property: JsonPropertyName("lastSuccess")] DateTimeOffset? LastSuccess,
    [property: JsonPropertyName("destinations")] IReadOnlyList<string> Destinations,
    [property: JsonPropertyName("problems")] IReadOnlyList<string> Problems)
{
    /// <summary>A day and a half: one missed nightly run is noticed the next morning.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(36);

    /// <summary>Null when no backup has run yet (or the file can't be read).</summary>
    public static BackupStatus? Read(AperturePaths paths)
    {
        try
        {
            using var file = File.OpenRead(paths.BackupStatusFile);
            return JsonSerializer.Deserialize<BackupStatus>(file);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public bool IsStale(DateTimeOffset now) => LastSuccess is not { } success || now - success > MaxAge;
}
