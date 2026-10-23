using System.Net;
using System.Text.Json;
using Aperture.Web.Configuration;
using Aperture.Web.Data;
using Aperture.Web.Data.Entities;
using Aperture.Web.Domain;
using Aperture.Web.Features.Backup;
using Aperture.Web.Features.Notifications;
using Aperture.Web.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aperture.Tests.Features;

public sealed class PushNotificationTests : IAsyncLifetime, IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 21, 10, 0, 0);
    private const string AppleEndpoint = "https://web.push.apple.com/QGuQyavXutnMH";

    private readonly TestDatabase _database = new();
    private readonly string _dataDirectory = Directory.CreateTempSubdirectory("aperture-push-").FullName;
    private readonly TestDevice _iPhone = TestDevice.Create();
    private readonly FakePushService _pushService = new();

    public async Task InitializeAsync()
    {
        await _database.InitializeAsync();
        await using var db = _database.CreateContext();
        await db.BusinessSettings.ExecuteUpdateAsync(s => s.SetProperty(x => x.Email, "studio@example.ch"));
    }

    public Task DisposeAsync() => _database.DisposeAsync();

    public void Dispose() => Directory.Delete(_dataDirectory, recursive: true);

    /// <summary>Answers like a push service; keeps what it was sent.</summary>
    private sealed class FakePushService : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, byte[] Body)> Received { get; } = [];

        public HttpStatusCode Status { get; set; } = HttpStatusCode.Created;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Received.Add((request, await request.Content!.ReadAsByteArrayAsync(cancellationToken)));
            return new HttpResponseMessage(Status);
        }
    }

    private sealed class HttpFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class DbFactory(TestDatabase database) : IDbContextFactory<ApertureDbContext>
    {
        public ApertureDbContext CreateDbContext() => database.CreateContext();
    }

    private PushService Push() => new(new HttpFactory(_pushService), new DbFactory(_database), new AperturePaths(_dataDirectory),
        new FixedTimeProvider(Now), NullLogger<PushService>.Instance);

    private PushReminders Reminders() => new(new DbFactory(_database), Push(), new AppLanguage(new DbFactory(_database)), NullLogger<PushReminders>.Instance);

    private PushMessage ReceivedMessage(int index = 0) =>
        JsonSerializer.Deserialize<PushMessage>(_iPhone.Decrypt(_pushService.Received[index].Body), JsonSerializerOptions.Web)!;

    private async Task AddOverdueInvoiceAsync(string number, string title = "Wedding")
    {
        await using var db = _database.CreateContext();
        db.Shoots.Add(new Shoot
        {
            Title = title, Client = new Client { Name = $"Client of {title}" }, Status = ShootStatus.Shot,
            Start = new DateTime(2026, 8, 1, 18, 0, 0), End = new DateTime(2026, 8, 1, 23, 0, 0),
            QuotedPrice = Money.FromChf(1000),
            Invoices =
            [
                new Invoice
                {
                    Number = number, Status = InvoiceStatus.Issued,
                    IssueDate = new DateOnly(2026, 8, 5), DueDate = new DateOnly(2026, 9, 4),
                    Lines = [new InvoiceLine { Description = "Photography", Quantity = 1, UnitPrice = Money.FromChf(1000) }],
                },
            ],
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_notification_reaches_the_device_encrypted_and_signed()
    {
        var push = Push();
        await push.SaveAsync(_iPhone.Subscription(AppleEndpoint), "iPhone");

        var delivered = await push.SendAsync(new PushMessage("Invoice 2026-001 is overdue", "CHF 1'000.00 open", "shoots/1?tab=invoice", "invoice-overdue:1"));

        Assert.Equal(1, delivered);
        var (request, _) = Assert.Single(_pushService.Received);
        Assert.Equal(AppleEndpoint, request.RequestUri!.ToString());
        Assert.Equal(["aes128gcm"], request.Content!.Headers.ContentEncoding);
        Assert.Equal("86400", Assert.Single(request.Headers.GetValues("TTL")));
        Assert.StartsWith("vapid t=", Assert.Single(request.Headers.GetValues("Authorization")));
        Assert.EndsWith($", k={push.PublicKey}", Assert.Single(request.Headers.GetValues("Authorization")));
        Assert.Equal(new PushMessage("Invoice 2026-001 is overdue", "CHF 1'000.00 open", "shoots/1?tab=invoice", "invoice-overdue:1"), ReceivedMessage());

        await using var db = _database.CreateContext();
        Assert.Equal(Now, (await db.PushSubscriptions.SingleAsync()).LastDeliveredAt);
    }

    [Fact]
    public async Task Subscribing_again_updates_the_device_instead_of_adding_one()
    {
        var push = Push();
        var first = await push.SaveAsync(_iPhone.Subscription(AppleEndpoint), "iPhone");
        var renewed = TestDevice.Create();

        var second = await push.SaveAsync(renewed.Subscription(AppleEndpoint), "iPhone");

        Assert.Equal(first.Id, second.Id);
        await using var db = _database.CreateContext();
        Assert.Equal(renewed.Subscription(AppleEndpoint).P256dh, (await db.PushSubscriptions.SingleAsync()).P256dh);
    }

    [Fact]
    public async Task Invalid_subscriptions_are_refused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Push().SaveAsync(_iPhone.Subscription("http://192.168.1.1/admin"), "iPhone"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Gone, 0)]
    [InlineData(HttpStatusCode.NotFound, 0)]
    [InlineData(HttpStatusCode.TooManyRequests, 1)]
    [InlineData(HttpStatusCode.Forbidden, 1)]
    public async Task Ended_subscriptions_are_dropped_other_failures_kept(HttpStatusCode status, int left)
    {
        var push = Push();
        await push.SaveAsync(_iPhone.Subscription(AppleEndpoint), "iPhone");
        _pushService.Status = status;

        Assert.Equal(0, await push.SendAsync(new PushMessage("Test", "", "")));

        await using var db = _database.CreateContext();
        Assert.Equal(left, await db.PushSubscriptions.CountAsync());
    }

    [Fact]
    public async Task Nothing_is_sent_without_a_contact_email()
    {
        var push = Push();
        await push.SaveAsync(_iPhone.Subscription(AppleEndpoint), "iPhone");
        await using (var db = _database.CreateContext())
        {
            await db.BusinessSettings.ExecuteUpdateAsync(s => s.SetProperty(x => x.Email, (string?)null));
        }

        Assert.Equal(0, await push.SendAsync(new PushMessage("Test", "", "")));
        Assert.Empty(_pushService.Received);
    }

    [Fact]
    public async Task An_overdue_invoice_is_sent_once()
    {
        await Push().SaveAsync(_iPhone.Subscription(AppleEndpoint), "iPhone");
        await AddOverdueInvoiceAsync("2026-001");

        await Reminders().SendDueAsync(Now, null, expectBackups: false, update: null);
        await Reminders().SendDueAsync(Now.AddHours(1), null, expectBackups: false, update: null);

        Assert.Single(_pushService.Received);
        var message = ReceivedMessage();
        Assert.Equal("Invoice 2026-001 is overdue", message.Title);
        Assert.Contains("open since 04.09.2026", message.Body);
        Assert.Equal("shoots/1?tab=invoice", message.Url);
    }

    [Fact]
    public async Task Nothing_is_sent_at_night_it_waits_for_the_morning()
    {
        await Push().SaveAsync(_iPhone.Subscription(AppleEndpoint), "iPhone");
        await AddOverdueInvoiceAsync("2026-001");

        await Reminders().SendDueAsync(new DateTime(2026, 10, 21, 3, 30, 0), null, expectBackups: false, update: null);
        Assert.Empty(_pushService.Received);

        await Reminders().SendDueAsync(new DateTime(2026, 10, 21, 8, 0, 0), null, expectBackups: false, update: null);
        Assert.Single(_pushService.Received);
    }

    [Fact]
    public async Task What_no_device_received_is_tried_again()
    {
        await Push().SaveAsync(_iPhone.Subscription(AppleEndpoint), "iPhone");
        await AddOverdueInvoiceAsync("2026-001");
        _pushService.Status = HttpStatusCode.ServiceUnavailable;

        await Reminders().SendDueAsync(Now, null, expectBackups: false, update: null);
        _pushService.Status = HttpStatusCode.Created;
        await Reminders().SendDueAsync(Now.AddMinutes(15), null, expectBackups: false, update: null);

        Assert.Equal(2, _pushService.Received.Count);
        await using var db = _database.CreateContext();
        Assert.Equal("invoice-overdue:1", (await db.SentNotifications.SingleAsync()).Key);
    }

    [Fact]
    public async Task Without_devices_nothing_is_marked_as_sent()
    {
        await AddOverdueInvoiceAsync("2026-001");

        await Reminders().SendDueAsync(Now, null, expectBackups: false, update: null);

        await using var db = _database.CreateContext();
        Assert.Empty(await db.SentNotifications.ToListAsync());
    }

    [Fact]
    public async Task Lots_of_news_at_once_goes_out_as_one_summary()
    {
        await Push().SaveAsync(_iPhone.Subscription(AppleEndpoint), "iPhone");
        foreach (var i in Enumerable.Range(1, 4))
        {
            await AddOverdueInvoiceAsync($"2026-00{i}", $"Wedding {i}");
        }

        await Reminders().SendDueAsync(Now, null, expectBackups: false, update: null);
        await Reminders().SendDueAsync(Now.AddHours(1), null, expectBackups: false, update: null);

        Assert.Single(_pushService.Received);
        Assert.Equal("4 things need attention", ReceivedMessage().Title);
        await using var db = _database.CreateContext();
        Assert.Equal(4, await db.SentNotifications.CountAsync());
    }

    [Fact]
    public async Task Open_prep_is_sent_three_days_before_the_shoot()
    {
        await Push().SaveAsync(_iPhone.Subscription(AppleEndpoint), "iPhone");
        var client = await _database.AddClientAsync();
        var shoot = await _database.AddShootAsync(client, new DateTime(2026, 10, 23, 18, 0, 0), new DateTime(2026, 10, 23, 23, 0, 0));
        var later = await _database.AddShootAsync(client, new DateTime(2026, 10, 30, 18, 0, 0), new DateTime(2026, 10, 30, 23, 0, 0), title: "Portrait");
        await using (var db = _database.CreateContext())
        {
            db.ChecklistItems.AddRange(
                new ChecklistItem { ShootId = shoot.Id, Text = "Family group list" },
                new ChecklistItem { ShootId = later.Id, Text = "Moodboard" });
            await db.SaveChangesAsync();
        }

        await Reminders().SendDueAsync(Now, null, expectBackups: false, update: null);

        Assert.Single(_pushService.Received);
        Assert.Equal("1 prep task open for Wedding", ReceivedMessage().Title);
        Assert.Equal($"shoots/{shoot.Id}?tab=prep", ReceivedMessage().Url);
    }

    [Fact]
    public async Task A_missing_backup_drive_is_news_again_after_the_next_run()
    {
        await Push().SaveAsync(_iPhone.Subscription(AppleEndpoint), "iPhone");
        BackupStatus Run(DateTimeOffset at) => new(at, at, ["/Volumes/Backup A"], ["/Volumes/Backup B is not connected"]);

        await Reminders().SendDueAsync(Now, Run(new DateTimeOffset(2026, 10, 21, 3, 30, 0, TimeSpan.Zero)), expectBackups: true, update: null);
        await Reminders().SendDueAsync(Now.AddHours(2), Run(new DateTimeOffset(2026, 10, 21, 3, 30, 0, TimeSpan.Zero)), expectBackups: true, update: null);
        await Reminders().SendDueAsync(Now.AddDays(1), Run(new DateTimeOffset(2026, 10, 22, 3, 30, 0, TimeSpan.Zero)), expectBackups: true, update: null);

        Assert.Equal(2, _pushService.Received.Count);
        Assert.Equal(new PushMessage("Backup problem", "/Volumes/Backup B is not connected", "settings",
            "backup-problem:2026-10-21T03:30:00.0000000+00:00:0"), ReceivedMessage());
    }

    [Fact]
    public async Task Shoots_to_invoice_or_mark_as_shot_stay_on_the_dashboard()
    {
        await Push().SaveAsync(_iPhone.Subscription(AppleEndpoint), "iPhone");
        var client = await _database.AddClientAsync();
        await _database.AddShootAsync(client, new DateTime(2026, 10, 10, 18, 0, 0), new DateTime(2026, 10, 10, 23, 0, 0));

        await Reminders().SendDueAsync(Now, null, expectBackups: false, update: null);

        await using var db = _database.CreateContext();
        Assert.Contains("Mark Wedding as shot", (await Aperture.Web.Features.Dashboard.Attention.ItemsAsync(db, Now)).Select(i => i.Text));
        Assert.Empty(_pushService.Received);
    }
}
