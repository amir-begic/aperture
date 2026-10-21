using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Funk.Web.Configuration;
using Funk.Web.Data;
using Funk.Web.Data.Entities;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Funk.Web.Features.Notifications;

/// <summary>
/// One notification. <paramref name="Url"/> is relative to Funk (like dashboard links) and opens on tap;
/// a second notification with the same <paramref name="Tag"/> replaces the first on the device.
/// </summary>
public sealed record PushMessage(string Title, string Body, string Url, string? Tag = null);

/// <summary>What a browser reports after subscribing (PushSubscription.toJSON() in wwwroot/push.js).</summary>
public sealed record BrowserSubscription(string Endpoint, string P256dh, string Auth);

/// <summary>
/// Sends notifications to the devices in Settings → Notifications, through their browser's push service
/// (Apple's for an iPhone). Push services hold a message for a day while a device is offline.
/// </summary>
public sealed class PushService(
    IHttpClientFactory httpFactory, IDbContextFactory<FunkDbContext> dbFactory, FunkPaths paths, TimeProvider time,
    ILogger<PushService> logger)
{
    /// <summary>How long a push service keeps trying to deliver.</summary>
    public static readonly TimeSpan TimeToLive = TimeSpan.FromDays(1);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Lazy<VapidKeys> _keys = new(() => VapidKeys.LoadOrCreate(paths));

    /// <summary>For the browser's subscribe call (applicationServerKey).</summary>
    public string PublicKey => _keys.Value.PublicKey;

    /// <summary>
    /// Push services want a contact address of whoever sends (Apple rejects messages without one): the email
    /// from Settings → Contact. Null while there is none.
    /// </summary>
    public static async Task<string?> SubjectAsync(FunkDbContext db)
    {
        var email = await db.BusinessSettings.Select(s => s.Email).SingleAsync();
        return string.IsNullOrWhiteSpace(email) ? null : $"mailto:{email.Trim()}";
    }

    /// <summary>Adds the browser's subscription, or updates it if the browser subscribed before.</summary>
    public async Task<PushSubscription> SaveAsync(BrowserSubscription browser, string device)
    {
        if (!IsValid(browser))
        {
            throw new ArgumentException("The browser sent an invalid subscription.", nameof(browser));
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        var subscription = await db.PushSubscriptions.SingleOrDefaultAsync(s => s.Endpoint == browser.Endpoint);
        if (subscription is null)
        {
            subscription = new PushSubscription { Endpoint = browser.Endpoint, CreatedAt = time.GetLocalNow().DateTime };
            db.PushSubscriptions.Add(subscription);
        }
        subscription.P256dh = browser.P256dh;
        subscription.Auth = browser.Auth;
        subscription.Device = device.Length > 100 ? device[..100] : device;
        await db.SaveChangesAsync();
        return subscription;
    }

    public async Task RemoveAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.PushSubscriptions.Where(s => s.Id == id).ExecuteDeleteAsync();
    }

    /// <summary>
    /// Sends to every device, or only to <paramref name="subscriptionId"/>. Returns how many push services took the
    /// message. Devices whose subscription has ended (the app was removed, notifications turned off) are dropped.
    /// </summary>
    public async Task<int> SendAsync(PushMessage message, int? subscriptionId = null, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (await SubjectAsync(db) is not { } subject)
        {
            logger.LogWarning("Notification not sent: no email under Settings → Contact");
            return 0;
        }

        var subscriptions = await db.PushSubscriptions
            .Where(s => subscriptionId == null || s.Id == subscriptionId)
            .ToListAsync(cancellationToken);
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, Json);
        var http = httpFactory.CreateClient(nameof(PushService));

        var delivered = 0;
        foreach (var subscription in subscriptions)
        {
            try
            {
                using var request = Request(subscription, payload, subject);
                using var response = await http.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    subscription.LastDeliveredAt = time.GetLocalNow().DateTime;
                    delivered++;
                }
                else if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    logger.LogInformation("Push subscription {Id} ({Device}) has ended; removing it", subscription.Id, subscription.Device);
                    db.PushSubscriptions.Remove(subscription);
                }
                else
                {
                    logger.LogWarning("Push service refused a notification for {Device}: {Status} {Reason}",
                        subscription.Device, (int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
                }
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(e, "Couldn't reach the push service for {Device}", subscription.Device);
            }
        }

        await db.SaveChangesAsync(CancellationToken.None);
        return delivered;
    }

    private HttpRequestMessage Request(PushSubscription subscription, byte[] payload, string subject)
    {
        var endpoint = new Uri(subscription.Endpoint);
        var body = WebPushEncryption.Encrypt(payload,
            WebEncoders.Base64UrlDecode(subscription.P256dh), WebEncoders.Base64UrlDecode(subscription.Auth));

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new ByteArrayContent(body),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentEncoding.Add("aes128gcm");
        request.Headers.TryAddWithoutValidation("Authorization", _keys.Value.AuthorizationHeader(endpoint, subject, time.GetUtcNow()));
        request.Headers.Add("TTL", ((int)TimeToLive.TotalSeconds).ToString());
        request.Headers.Add("Urgency", "normal");
        return request;
    }

    /// <summary>An https endpoint and keys of the right size, so nothing else ends up being sent requests.</summary>
    public static bool IsValid(BrowserSubscription browser)
    {
        if (!Uri.TryCreate(browser.Endpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme != Uri.UriSchemeHttps
            || browser.Endpoint.Length > 2048)
        {
            return false;
        }

        try
        {
            return WebEncoders.Base64UrlDecode(browser.Auth).Length == 16
                && WebPushEncryption.IsValidPublicKey(WebEncoders.Base64UrlDecode(browser.P256dh));
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
