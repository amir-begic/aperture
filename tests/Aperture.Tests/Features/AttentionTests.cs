using Aperture.Web.Data.Entities;
using Aperture.Web.Domain;
using Aperture.Web.Features.Dashboard;

namespace Aperture.Tests.Features;

public sealed class AttentionTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0);
    private readonly TestDatabase _database = new();

    public Task InitializeAsync() => _database.InitializeAsync();

    public Task DisposeAsync() => _database.DisposeAsync();

    private async Task<Shoot> AddAsync(string title, DateTime start, ShootStatus status, decimal? quote = 1000,
        Action<Shoot>? configure = null)
    {
        await using var db = _database.CreateContext();
        var shoot = new Shoot
        {
            Title = title, Client = new Client { Name = $"Client of {title}" }, Status = status,
            Start = start, End = start.AddHours(5), QuotedPrice = quote is { } q ? Money.FromChf(q) : null,
        };
        configure?.Invoke(shoot);
        db.Shoots.Add(shoot);
        await db.SaveChangesAsync();
        return shoot;
    }

    private async Task<List<AttentionItem>> ItemsAsync()
    {
        await using var db = _database.CreateContext();
        return await Attention.ItemsAsync(db, Now);
    }

    [Fact]
    public async Task Overdue_invoice_is_urgent()
    {
        await AddAsync("Wedding", new DateTime(2026, 8, 1, 18, 0, 0), ShootStatus.Done, configure: shoot =>
            shoot.Invoices.Add(new Invoice
            {
                Number = "2026-001", Status = InvoiceStatus.Issued,
                IssueDate = new DateOnly(2026, 8, 5), DueDate = new DateOnly(2026, 9, 4),
                Lines = [new InvoiceLine { Description = "DJ", Quantity = 1, UnitPrice = Money.FromChf(1000) }],
            }));

        var item = Assert.Single(await ItemsAsync());

        Assert.Equal(AttentionLevel.Urgent, item.Level);
        Assert.Equal("Invoice 2026-001 is overdue", item.Text);
        Assert.Contains("CHF 1'000.00 open since 04.09.2026", item.Detail);
    }

    [Fact]
    public async Task Invoices_sent_before_the_shoot_can_be_overdue_too()
    {
        await AddAsync("Wedding in December", new DateTime(2026, 12, 12, 18, 0, 0), ShootStatus.Confirmed, configure: shoot =>
            shoot.Invoices.Add(new Invoice
            {
                Number = "2026-002", Status = InvoiceStatus.Issued,
                IssueDate = new DateOnly(2026, 9, 1), DueDate = new DateOnly(2026, 10, 1),
                Lines = [new InvoiceLine { Description = "DJ", Quantity = 1, UnitPrice = Money.FromChf(1000) }],
            }));

        var item = Assert.Single(await ItemsAsync());

        Assert.Equal("Invoice 2026-002 is overdue", item.Text);
    }

    [Fact]
    public async Task Played_shoot_without_invoice_and_one_still_marked_confirmed()
    {
        await AddAsync("Club night", new DateTime(2026, 9, 26, 22, 0, 0), ShootStatus.Confirmed);

        var items = await ItemsAsync();

        Assert.Equal(["Club night has no invoice yet", "Mark Club night as done"], items.Select(i => i.Text));
    }

    [Fact]
    public async Task Paid_or_unpriced_shoots_need_no_invoice_reminder()
    {
        await AddAsync("Paid in cash", new DateTime(2026, 9, 1, 20, 0, 0), ShootStatus.Done, configure: shoot =>
            shoot.Payments.Add(new Payment { Date = new DateOnly(2026, 9, 1), Amount = Money.FromChf(1000), Method = PaymentMethod.Cash }));
        await AddAsync("Favour for a friend", new DateTime(2026, 9, 5, 20, 0, 0), ShootStatus.Done, quote: null);
        await AddAsync("Old inquiry", new DateTime(2026, 9, 12, 20, 0, 0), ShootStatus.Inquiry);

        Assert.Empty(await ItemsAsync());
    }

    [Fact]
    public async Task Open_prep_tasks_within_two_weeks()
    {
        await AddAsync("Wedding soon", new DateTime(2026, 10, 10, 15, 0, 0), ShootStatus.Confirmed, configure: shoot =>
            shoot.ChecklistItems.AddRange([
                new ChecklistItem { Text = "First dance" },
                new ChecklistItem { Text = "Timeline", IsDone = true },
                new ChecklistItem { Text = "Do-not-play list" },
            ]));
        await AddAsync("Wedding later", new DateTime(2026, 12, 12, 15, 0, 0), ShootStatus.Confirmed, configure: shoot =>
            shoot.ChecklistItems.Add(new ChecklistItem { Text = "First dance" }));

        var item = Assert.Single(await ItemsAsync());

        Assert.Equal("2 prep tasks open for Wedding soon", item.Text);
        Assert.Equal($"shoots/{1}?tab=prep", item.Link);
    }
}
