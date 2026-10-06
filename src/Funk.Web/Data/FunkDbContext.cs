using Funk.Web.Data.Converters;
using Funk.Web.Data.Entities;
using Funk.Web.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Funk.Web.Data;

public class FunkDbContext(DbContextOptions<FunkDbContext> options) : DbContext(options)
{
    public DbSet<BusinessSettings> BusinessSettings => Set<BusinessSettings>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<Gig> Gigs => Set<Gig>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PlaylistLink> PlaylistLinks => Set<PlaylistLink>();
    public DbSet<ChecklistItem> ChecklistItems => Set<ChecklistItem>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();

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
        configurationBuilder.Properties<ExpenseCategory>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<PaymentMethod>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<InvoiceStatus>().HaveConversion<string>().HaveMaxLength(20);
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

            // Financial records block deleting a gig; prep data goes with it.
            // Attachment rows cascade too, but their files are removed in code (see GigDeletion).
            gig.HasMany(g => g.Expenses).WithOne(e => e.Gig).OnDelete(DeleteBehavior.Restrict);
            gig.HasMany(g => g.Payments).WithOne(p => p.Gig).OnDelete(DeleteBehavior.Restrict);
            gig.HasMany(g => g.Invoices).WithOne(i => i.Gig).OnDelete(DeleteBehavior.Restrict);
            gig.HasMany(g => g.PlaylistLinks).WithOne(l => l.Gig).OnDelete(DeleteBehavior.Cascade);
            gig.HasMany(g => g.ChecklistItems).WithOne(i => i.Gig).OnDelete(DeleteBehavior.Cascade);
            gig.HasMany(g => g.Attachments).WithOne(a => a.Gig).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Expense>(expense =>
        {
            expense.HasIndex(e => e.Date);
            expense.HasOne(e => e.Receipt).WithMany().OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Invoice>(invoice =>
        {
            // Unique among issued invoices; drafts have no number yet (SQLite allows many NULLs in a unique index).
            invoice.HasIndex(i => i.Number).IsUnique();
            invoice.Property(i => i.Number).HasMaxLength(20);
            invoice.ComplexProperty(i => i.Seller, ConfigureAddress);
            invoice.ComplexProperty(i => i.Client, ConfigureAddress);
            invoice.Property(i => i.SellerIban).HasMaxLength(34);
            invoice.Property(i => i.SellerEmail).HasMaxLength(200);
            invoice.Property(i => i.SellerPhone).HasMaxLength(50);
            invoice.Property(i => i.SellerVatNumber).HasMaxLength(32);
            invoice.HasMany(i => i.Lines).WithOne(l => l.Invoice).OnDelete(DeleteBehavior.Cascade);
            invoice.HasOne(i => i.Pdf).WithMany().OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Payment>(payment =>
        {
            payment.HasIndex(p => p.Date);
            payment.HasOne(p => p.Proof).WithMany().OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureAddress(ComplexPropertyBuilder<InvoiceAddress> address)
    {
        address.Property(a => a.Name).HasMaxLength(150);
        address.Property(a => a.Company).HasMaxLength(150);
        address.Property(a => a.Street).HasMaxLength(100);
        address.Property(a => a.PostalCode).HasMaxLength(16);
        address.Property(a => a.City).HasMaxLength(100);
        address.Property(a => a.CountryCode).HasMaxLength(2);
    }
}
