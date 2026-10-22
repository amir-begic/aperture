using Microsoft.Data.Sqlite;

namespace Aperture.Web.Installation;

/// <summary>Consistent copies of the database, also while Aperture writes to it (SQLite's online backup).</summary>
public static class DatabaseCopy
{
    public static void Copy(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var from = new SqliteConnection(Connection(source, SqliteOpenMode.ReadOnly));
        using var to = new SqliteConnection(Connection(target, SqliteOpenMode.ReadWriteCreate));
        from.Open();
        to.Open();
        from.BackupDatabase(to);
    }

    /// <summary>"ok", or what SQLite found wrong.</summary>
    public static string IntegrityCheck(string database)
    {
        using var connection = new SqliteConnection(Connection(database, SqliteOpenMode.ReadOnly));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        return string.Join("; ", ReadAll(command));
    }

    /// <summary>Puts a copy in place of the live database. Aperture must be stopped.</summary>
    public static void Restore(string copy, string database)
    {
        File.Copy(copy, database, overwrite: true);
        File.Delete(database + "-wal");
        File.Delete(database + "-shm");
    }

    // Unpooled, so the files are closed when the connection is: they're copied or deleted right after.
    private static string Connection(string path, SqliteOpenMode mode) =>
        new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false }.ToString();

    private static IEnumerable<string> ReadAll(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            yield return reader.GetString(0);
        }
    }
}
