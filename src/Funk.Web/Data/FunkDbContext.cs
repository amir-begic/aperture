using Funk.Web.Data.Converters;
using Funk.Web.Data.Entities;
using Funk.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace Funk.Web.Data;

public class FunkDbContext(DbContextOptions<FunkDbContext> options) : DbContext(options)
{
    public DbSet<BusinessSettings> BusinessSettings => Set<BusinessSettings>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Money>().HaveConversion<MoneyConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The settings row always exists, so pages can load it without a create path.
        modelBuilder.Entity<BusinessSettings>().HasData(new BusinessSettings());
    }
}
