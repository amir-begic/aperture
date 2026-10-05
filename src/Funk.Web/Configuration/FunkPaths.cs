using Microsoft.Data.Sqlite;

namespace Funk.Web.Configuration;

/// <summary>
/// Where the app keeps its data. Configured via <c>Funk:DataDirectory</c>;
/// kept outside the app folder in production so deployments never touch it.
/// </summary>
public sealed record FunkPaths(string DataDirectory)
{
    public string DatabasePath => Path.Combine(DataDirectory, "funk.db");

    public string ConnectionString =>
        new SqliteConnectionStringBuilder { DataSource = DatabasePath }.ToString();

    public static FunkPaths From(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration["Funk:DataDirectory"] ?? "data";

        // Allow "~/funk-data" in the Mac mini config.
        if (configured.StartsWith('~'))
        {
            configured = Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), configured[1..]);
        }

        return new FunkPaths(Path.GetFullPath(configured, environment.ContentRootPath));
    }
}
