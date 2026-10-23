using Aperture.Web.Data.Converters;
using Aperture.Web.Data.Entities;
using Aperture.Web.Domain;
using Aperture.Web.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Aperture.Web.Data;

public class ApertureDbContext(DbContextOptions<ApertureDbContext> options) : DbContext(options)
{
    public DbSet<BusinessSettings> BusinessSettings => Set<BusinessSettings>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Shoot> Shoots => Set<Shoot>();
    public DbSet<Package> Packages => Set<Package>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<ReferenceLink> ReferenceLinks => Set<ReferenceLink>();
    public DbSet<ChecklistItem> ChecklistItems => Set<ChecklistItem>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<QuoteLine> QuoteLines => Set<QuoteLine>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<SentNotification> SentNotifications => Set<SentNotification>();

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
        configurationBuilder.Properties<ShootStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<ShootType>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<ExpenseCategory>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<PaymentMethod>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<InvoiceStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<QuoteStatus>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<AssetCategory>().HaveConversion<string>().HaveMaxLength(20);
        configurationBuilder.Properties<Language>().HaveConversion<string>().HaveMaxLength(20);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The settings row always exists, so pages can load it without a create path.
        modelBuilder.Entity<BusinessSettings>().HasData(new BusinessSettings());

        modelBuilder.Entity<Client>().HasIndex(c => c.Name);
        modelBuilder.Entity<Location>().HasIndex(l => l.Name);
        modelBuilder.Entity<Package>().HasIndex(p => p.Name);

        modelBuilder.Entity<Shoot>(shoot =>
        {
            shoot.HasIndex(s => s.Start);

            // Never lose a shoot's history by deleting its client or location.
            shoot.HasOne(s => s.Client).WithMany(c => c.Shoots).OnDelete(DeleteBehavior.Restrict);
            shoot.HasOne(s => s.Location).WithMany(l => l.Shoots).OnDelete(DeleteBehavior.Restrict);
            // A shoot keeps what was copied from its package, so the package can go.
            shoot.HasOne(s => s.Package).WithMany(p => p.Shoots).OnDelete(DeleteBehavior.SetNull);

            // Financial records block deleting a shoot; prep data goes with it.
            // Attachment rows cascade too, but their files are removed in code (see ShootDeletion).
            shoot.HasMany(s => s.Expenses).WithOne(e => e.Shoot).OnDelete(DeleteBehavior.Restrict);
            shoot.HasMany(s => s.Payments).WithOne(p => p.Shoot).OnDelete(DeleteBehavior.Restrict);
            shoot.HasMany(s => s.Invoices).WithOne(i => i.Shoot).OnDelete(DeleteBehavior.Restrict);
            // Quotes aren't financial records: they go with the shoot, like its files.
            shoot.HasMany(s => s.Quotes).WithOne(q => q.Shoot).OnDelete(DeleteBehavior.Cascade);
            shoot.HasMany(s => s.ReferenceLinks).WithOne(l => l.Shoot).OnDelete(DeleteBehavior.Cascade);
            shoot.HasMany(s => s.ChecklistItems).WithOne(i => i.Shoot).OnDelete(DeleteBehavior.Cascade);
            shoot.HasMany(s => s.Attachments).WithOne(a => a.Shoot).OnDelete(DeleteBehavior.Cascade);
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

        modelBuilder.Entity<Quote>(quote =>
        {
            quote.HasIndex(q => q.Number).IsUnique();
            quote.Property(q => q.Number).HasMaxLength(20);
            quote.ComplexProperty(q => q.Seller, ConfigureAddress);
            quote.ComplexProperty(q => q.Client, ConfigureAddress);
            quote.Property(q => q.SellerEmail).HasMaxLength(200);
            quote.Property(q => q.SellerPhone).HasMaxLength(50);
            quote.Property(q => q.SellerVatNumber).HasMaxLength(32);
            quote.HasMany(q => q.Lines).WithOne(l => l.Quote).OnDelete(DeleteBehavior.Cascade);
            // The PDF is one of the shoot's files and goes with it; the quote just forgets it.
            quote.HasOne(q => q.Pdf).WithMany().OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Asset>(asset =>
        {
            asset.HasIndex(a => a.PurchaseDate);
            asset.HasOne(a => a.Receipt).WithMany().OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Payment>(payment =>
        {
            payment.HasIndex(p => p.Date);
            payment.HasOne(p => p.Proof).WithMany().OnDelete(DeleteBehavior.SetNull);
        });

        // A browser subscribing again gets the same endpoint: update the row instead of adding a second one.
        modelBuilder.Entity<PushSubscription>().HasIndex(s => s.Endpoint).IsUnique();
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
