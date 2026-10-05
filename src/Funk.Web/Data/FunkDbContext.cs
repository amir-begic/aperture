using Funk.Web.Data.Converters;
using Funk.Web.Data.Entities;
using Funk.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace Funk.Web.Data;

public class FunkDbContext(DbContextOptions<FunkDbContext> options) : DbContext(options)
{
    public DbSet<BusinessSettings> BusinessSettings => Set<BusinessSettings>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<Gig> Gigs => Set<Gig>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ClearBlankOptionalText();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ClearBlankOptionalText();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// A cleared input sends "", so optional text would be stored as either "" or null.
    /// Store null for both, so queries like <c>Company == null</c> mean "not provided".
    /// </summary>
    private void ClearBlankOptionalText()
    {
        var changed = ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified);
        foreach (var property in changed.SelectMany(e => e.Properties))
        {
            if (property.Metadata.ClrType == typeof(string)
                && property.Metadata.IsNullable
                && property.CurrentValue is string text
                && string.IsNullOrWhiteSpace(text))
            {
                property.CurrentValue = null;
            }
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Money>().HaveConversion<MoneyConverter>();

        // Enums as text: readable in the database and safe to reorder.
        configurationBuilder.Properties<GigStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<EventType>().HaveConversion<string>().HaveMaxLength(20);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The settings row always exists, so pages can load it without a create path.
        modelBuilder.Entity<BusinessSettings>().HasData(new BusinessSettings());

        modelBuilder.Entity<Client>().HasIndex(c => c.Name);
        modelBuilder.Entity<Venue>().HasIndex(v => v.Name);

        modelBuilder.Entity<Gig>(gig =>
        {
            gig.HasIndex(g => g.Start);

            // Never lose a gig's history by deleting its client or venue.
            gig.HasOne(g => g.Client).WithMany(c => c.Gigs).OnDelete(DeleteBehavior.Restrict);
            gig.HasOne(g => g.Venue).WithMany(v => v.Gigs).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
