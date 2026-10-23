using Aperture.Web.Configuration;
using Aperture.Web.Data;
using Aperture.Web.Data.Entities;
using Aperture.Web.Features.Backup;
using Aperture.Web.Features.Dashboard;
using Aperture.Web.Features.Updates;
using Aperture.Web.Localization;
using Microsoft.EntityFrameworkCore;

namespace Aperture.Web.Features.Notifications;

/// <summary>
/// Turns what the dashboard flags into notifications, each once: overdue invoices, deliveries due within a week or
/// overdue, inquiries waiting a week for an answer, open prep a few days before a shoot, backup problems and new
/// versions of Aperture. Shoots to invoice or to mark as shot, and past inquiries, stay on the dashboard only.
/// In the language chosen in Settings.
/// </summary>
public sealed class PushReminders(
    IDbContextFactory<ApertureDbContext> dbFactory, PushService push, AppLanguage language, ILogger<PushReminders> logger)
{
    /// <summary>Open prep is pushed this close to a shoot (the dashboard shows it two weeks ahead).</summary>
    public static readonly TimeSpan PrepHorizon = TimeSpan.FromDays(3);

    /// <summary>Nothing is sent at night: what comes up then waits for the morning.</summary>
    public static readonly TimeOnly DayStarts = new(8, 0);
    public static readonly TimeOnly DayEnds = new(21, 0);

    /// <summary>More news than this at once (e.g. right after turning notifications on) goes out as one summary.</summary>
    public const int MaxSeparate = 3;

    public static bool IsDaytime(DateTime now) => TimeOnly.FromDateTime(now).IsBetween(DayStarts, DayEnds);

    /// <summary>Sends what's new since the last run. Something no device received is tried again next time.</summary>
    public async Task SendDueAsync(DateTime now, BackupStatus? backup, bool expectBackups, Release? update,
        CancellationToken cancellationToken = default)
    {
        if (!IsDaytime(now))
        {
            return;
        }

        using var _ = Languages.Use(language.ForBackground);
        List<AttentionItem> due;
        await using (var db = await dbFactory.CreateDbContextAsync(cancellationToken))
        {
            if (!await db.PushSubscriptions.AnyAsync(cancellationToken))
            {
                return;
            }

            var items = (await Attention.ItemsAsync(db, now, backup, expectBackups, update, PrepHorizon))
                .Where(i => i.PushKey is not null)
                .ToList();
            var keys = items.Select(i => i.PushKey!).ToList();
            var sent = (await db.SentNotifications.Where(s => keys.Contains(s.Key)).Select(s => s.Key).ToListAsync(cancellationToken))
                .ToHashSet();
            due = items.Where(i => !sent.Contains(i.PushKey!)).ToList();
        }

        if (due.Count > MaxSeparate)
        {
            var summary = new PushMessage(Texts.Format("{0} things need attention", due.Count),
                string.Join(" · ", due.Take(MaxSeparate).Select(i => i.Text)) + " …", "", "attention");
            await SendAsync(summary, due, now, cancellationToken);
            return;
        }

        foreach (var item in due)
        {
            await SendAsync(new PushMessage(item.Text, item.Detail, item.Link, item.PushKey), [item], now, cancellationToken);
        }
    }

    private async Task SendAsync(PushMessage message, IReadOnlyList<AttentionItem> about, DateTime now, CancellationToken cancellationToken)
    {
        if (await push.SendAsync(message, cancellationToken: cancellationToken) == 0)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        db.SentNotifications.AddRange(about.Select(i => new SentNotification { Key = i.PushKey!, SentAt = now }));
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Notification sent: {Title}", message.Title);
    }
}

/// <summary>Looks for news every 15 minutes, starting shortly after the first update check.</summary>
public sealed class PushReminderWorker(
    PushReminders reminders, UpdateService updates, AperturePaths paths, IHostEnvironment environment, TimeProvider time,
    ILogger<PushReminderWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await reminders.SendDueAsync(time.GetLocalNow().DateTime, BackupStatus.Read(paths),
                        expectBackups: !environment.IsDevelopment(), updates.Available, stoppingToken);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // Try again next round rather than stop the worker (and with it, the app).
                    logger.LogError(e, "Sending notifications failed");
                }
                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }
}
