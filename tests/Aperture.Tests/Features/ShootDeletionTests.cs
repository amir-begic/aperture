using System.Text;
using Aperture.Web.Configuration;
using Aperture.Web.Data.Entities;
using Aperture.Web.Features.Attachments;
using Aperture.Web.Features.Shoots;
using Microsoft.EntityFrameworkCore;

namespace Aperture.Tests.Features;

public sealed class ShootDeletionTests : IAsyncLifetime
{
    private readonly TestDatabase _database = new();
    private readonly string _dataDirectory = Directory.CreateTempSubdirectory("aperture-tests-").FullName;
    private AttachmentStore _store = null!;

    public Task InitializeAsync()
    {
        _store = new AttachmentStore(new AperturePaths(_dataDirectory), new FixedTimeProvider(new DateTime(2026, 10, 6)));
        return _database.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        await _database.DisposeAsync();
        Directory.Delete(_dataDirectory, recursive: true);
    }

    private async Task<Shoot> AddShootAsync()
    {
        var client = await _database.AddClientAsync();
        return await _database.AddShootAsync(client, new DateTime(2026, 12, 12, 20, 0, 0), new DateTime(2026, 12, 13, 2, 0, 0));
    }

    [Fact]
    public async Task Deleting_a_shoot_removes_its_prep_data_and_files()
    {
        var shoot = await AddShootAsync();
        var file = await _store.SaveAsync(new MemoryStream(Encoding.UTF8.GetBytes("timeline")), "timeline.pdf", shoot.Id);
        await using (var db = _database.CreateContext())
        {
            db.Attachments.Add(file);
            db.PlaylistLinks.Add(new PlaylistLink { ShootId = shoot.Id, Url = "https://open.spotify.com/playlist/1" });
            db.ChecklistItems.Add(new ChecklistItem { ShootId = shoot.Id, Text = "First dance" });
            await db.SaveChangesAsync();
        }

        await using (var db = _database.CreateContext())
        {
            Assert.False(await db.HasFinancialRecordsAsync(shoot.Id));
            await db.DeleteShootAsync(shoot.Id, _store);
        }

        await using (var db = _database.CreateContext())
        {
            Assert.Empty(await db.Shoots.ToListAsync());
            Assert.Empty(await db.PlaylistLinks.ToListAsync());
            Assert.Empty(await db.ChecklistItems.ToListAsync());
            Assert.Empty(await db.Attachments.ToListAsync());
        }
        Assert.False(File.Exists(_store.GetFullPath(file)));
    }

    [Fact]
    public async Task Shoots_with_payments_are_financial_records_and_cannot_be_deleted()
    {
        var shoot = await AddShootAsync();
        await _database.AddPaymentAsync(shoot, 500);

        await using var db = _database.CreateContext();
        Assert.True(await db.HasFinancialRecordsAsync(shoot.Id));
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => db.DeleteShootAsync(shoot.Id, _store));
    }

    [Fact]
    public async Task Shoots_with_costs_are_financial_records()
    {
        var shoot = await AddShootAsync();
        await using var db = _database.CreateContext();
        db.Expenses.Add(new Expense { ShootId = shoot.Id, Description = "Lights", Amount = Aperture.Web.Domain.Money.FromChf(120), Date = new DateOnly(2026, 12, 12) });
        await db.SaveChangesAsync();

        Assert.True(await db.HasFinancialRecordsAsync(shoot.Id));
    }

    [Fact]
    public async Task Shoot_rows_include_what_was_received()
    {
        var shoot = await AddShootAsync();
        await using (var db = _database.CreateContext())
        {
            (await db.Shoots.SingleAsync()).QuotedPrice = Aperture.Web.Domain.Money.FromChf(1800);
            await db.SaveChangesAsync();
        }
        await _database.AddPaymentAsync(shoot, 500);
        await _database.AddPaymentAsync(shoot, 300.50m);

        await using (var db = _database.CreateContext())
        {
            var row = (await db.Shoots.ToRowsAsync(db)).Single();
            Assert.Equal(Aperture.Web.Domain.Money.FromChf(800.50m), row.Received);
            Assert.Equal(Aperture.Web.Domain.PaymentState.PartiallyPaid, row.PaymentState);
        }
    }

    [Fact]
    public async Task Shoot_rows_include_costs_billed_on_top_of_the_quote()
    {
        var shoot = await AddShootAsync();
        await using (var db = _database.CreateContext())
        {
            (await db.Shoots.SingleAsync()).QuotedPrice = Aperture.Web.Domain.Money.FromChf(1800);
            db.Expenses.Add(new Expense { ShootId = shoot.Id, Description = "Lights", Date = new DateOnly(2026, 12, 12),
                Amount = Aperture.Web.Domain.Money.FromChf(350), BilledToClient = Aperture.Web.Domain.Money.FromChf(450) });
            db.Expenses.Add(new Expense { ShootId = shoot.Id, Description = "PA", Date = new DateOnly(2026, 12, 12),
                Amount = Aperture.Web.Domain.Money.FromChf(200) });
            await db.SaveChangesAsync();
        }
        await _database.AddPaymentAsync(shoot, 1800);

        await using (var db = _database.CreateContext())
        {
            var row = (await db.Shoots.ToRowsAsync(db)).Single();
            Assert.Equal(Aperture.Web.Domain.Money.FromChf(450), row.BilledCosts);
            Assert.Equal(Aperture.Web.Domain.PaymentState.PartiallyPaid, row.PaymentState); // quote paid, billed light rental not yet
        }
    }
}
