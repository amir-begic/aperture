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
        await AddAsync("Wedding", new DateTime(2026, 8, 1, 18, 0, 0), ShootStatus.Shot, configure: shoot =>
            shoot.Invoices.Add(new Invoice
            {
                Number = "2026-001", Status = InvoiceStatus.Issued,
                IssueDate = new DateOnly(2026, 8, 5), DueDate = new DateOnly(2026, 9, 4),
                Lines = [new InvoiceLine { Description = "Photography", Quantity = 1, UnitPrice = Money.FromChf(1000) }],
            }));

        var item = Assert.Single(await ItemsAsync());

        Assert.Equal(AttentionLevel.Urgent, item.Level);
        Assert.Equal("Invoice 2026-001 is overdue", item.Text);
        Assert.Contains("CHF 1'000.00 open since 04.09.2026", item.Detail);
    }

    [Fact]
    public async Task Invoices_sent_before_the_shoot_can_be_overdue_too()
    {
        await AddAsync("Wedding in December", new DateTime(2026, 12, 12, 18, 0, 0), ShootStatus.Booked, configure: shoot =>
            shoot.Invoices.Add(new Invoice
            {
                Number = "2026-002", Status = InvoiceStatus.Issued,
                IssueDate = new DateOnly(2026, 9, 1), DueDate = new DateOnly(2026, 10, 1),
                Lines = [new InvoiceLine { Description = "Photography", Quantity = 1, UnitPrice = Money.FromChf(1000) }],
            }));

        var item = Assert.Single(await ItemsAsync());

        Assert.Equal("Invoice 2026-002 is overdue", item.Text);
    }

    [Fact]
    public async Task Past_shoot_without_invoice_and_one_still_marked_booked()
    {
        await AddAsync("Headshots", new DateTime(2026, 9, 26, 22, 0, 0), ShootStatus.Booked);

        var items = await ItemsAsync();

        Assert.Equal(["Headshots has no invoice yet", "Mark Headshots as shot"], items.Select(i => i.Text));
    }

    [Fact]
    public async Task Overdue_delivery_is_urgent_until_delivered()
    {
        var late = await AddAsync("Wedding Rossi", new DateTime(2026, 9, 5, 14, 0, 0), ShootStatus.Shot, quote: null,
            configure: shoot => shoot.DeliveryDue = new DateOnly(2026, 10, 1));
        await AddAsync("Wedding Meier", new DateTime(2026, 9, 12, 14, 0, 0), ShootStatus.Delivered, quote: null,
            configure: shoot => shoot.DeliveryDue = new DateOnly(2026, 10, 1));

        var item = Assert.Single(await ItemsAsync());

        Assert.Equal(AttentionLevel.Urgent, item.Level);
        Assert.Equal("Delivery for Wedding Rossi is overdue", item.Text);
        Assert.Equal("Due Thu 01.10.2026 · Client of Wedding Rossi", item.Detail);
        Assert.Equal($"delivery-overdue:{late.Id}:2026-10-01", item.PushKey);
    }

    [Fact]
    public async Task Delivery_due_within_a_week()
    {
        await AddAsync("Portrait Keller", new DateTime(2026, 9, 26, 10, 0, 0), ShootStatus.Shot, quote: null,
            configure: shoot => shoot.DeliveryDue = new DateOnly(2026, 10, 10));
        await AddAsync("Family Weber", new DateTime(2026, 10, 3, 10, 0, 0), ShootStatus.Shot, quote: null,
            configure: shoot => shoot.DeliveryDue = new DateOnly(2026, 10, 31));

        var item = Assert.Single(await ItemsAsync());

        Assert.Equal(AttentionLevel.Warning, item.Level);
        Assert.Equal("Deliver Portrait Keller by Sat 10.10.", item.Text);
        Assert.StartsWith("delivery-due:", item.PushKey);
    }

    [Fact]
    public async Task Paid_or_unpriced_shoots_need_no_invoice_reminder()
    {
        await AddAsync("Paid in cash", new DateTime(2026, 9, 1, 20, 0, 0), ShootStatus.Shot, configure: shoot =>
            shoot.Payments.Add(new Payment { Date = new DateOnly(2026, 9, 1), Amount = Money.FromChf(1000), Method = PaymentMethod.Cash }));
        await AddAsync("Favour for a friend", new DateTime(2026, 9, 5, 20, 0, 0), ShootStatus.Shot, quote: null);
        await AddAsync("Old inquiry", new DateTime(2026, 9, 12, 20, 0, 0), ShootStatus.Inquiry);

        // Only the reminder to close the inquiry: it was never booked, so there's nothing to invoice.
        Assert.Equal(["Old inquiry is still an inquiry"], (await ItemsAsync()).Select(i => i.Text));
    }

    [Fact]
    public async Task Inquiry_without_an_answer_after_a_week_needs_a_follow_up()
    {
        await AddAsync("Wedding Keller", new DateTime(2027, 6, 5, 13, 0, 0), ShootStatus.Inquiry,
            configure: shoot => shoot.CreatedAt = Now.AddDays(-Attention.InquiryFollowUpDays));
        await AddAsync("Fresh inquiry", new DateTime(2027, 6, 12, 13, 0, 0), ShootStatus.Inquiry,
            configure: shoot => shoot.CreatedAt = Now.AddDays(-2));

        var item = Assert.Single(await ItemsAsync());

        Assert.Equal(AttentionLevel.Warning, item.Level);
        Assert.Equal("Follow up on Wedding Keller", item.Text);
        Assert.Equal("Inquiry since 29.09.2026, for Sat 05.06.2027 · Client of Wedding Keller", item.Detail);
        Assert.NotNull(item.PushKey);
    }

    [Fact]
    public async Task Past_inquiry_is_to_be_booked_or_cancelled_without_a_notification()
    {
        await AddAsync("Portrait Meier", new DateTime(2026, 10, 1, 10, 0, 0), ShootStatus.Inquiry,
            configure: shoot => shoot.CreatedAt = new DateTime(2026, 9, 1));

        var item = Assert.Single(await ItemsAsync());

        Assert.Equal("Portrait Meier is still an inquiry", item.Text);
        Assert.Null(item.PushKey);
    }

    [Fact]
    public async Task Open_prep_tasks_within_two_weeks()
    {
        await AddAsync("Wedding soon", new DateTime(2026, 10, 10, 15, 0, 0), ShootStatus.Booked, configure: shoot =>
            shoot.ChecklistItems.AddRange([
                new ChecklistItem { Text = "Family group list" },
                new ChecklistItem { Text = "Timeline", IsDone = true },
                new ChecklistItem { Text = "Shot list" },
            ]));
        await AddAsync("Wedding later", new DateTime(2026, 12, 12, 15, 0, 0), ShootStatus.Booked, configure: shoot =>
            shoot.ChecklistItems.Add(new ChecklistItem { Text = "Family group list" }));

        var item = Assert.Single(await ItemsAsync());

        Assert.Equal("2 prep tasks open for Wedding soon", item.Text);
        Assert.Equal($"shoots/{1}?tab=prep", item.Link);
    }
}
