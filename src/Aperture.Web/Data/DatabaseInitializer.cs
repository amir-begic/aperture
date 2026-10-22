using Aperture.Web.Configuration;
using Microsoft.EntityFrameworkCore;

namespace Aperture.Web.Data;

public static class DatabaseInitializer
{
    /// <summary>Creates the data directory and applies pending migrations on startup.</summary>
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        var paths = app.Services.GetRequiredService<AperturePaths>();
        Directory.CreateDirectory(paths.DataDirectory);

        var factory = app.Services.GetRequiredService<IDbContextFactory<ApertureDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();

        // WAL lets the backup read the database while the app writes to it.
        // The mode is stored in the database file, so this only changes something once.
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    }
}
