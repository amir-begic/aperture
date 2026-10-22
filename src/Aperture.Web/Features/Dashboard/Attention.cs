using Aperture.Web.Data;
using Aperture.Web.Data.Entities;
using Aperture.Web.Domain;
using Aperture.Web.Features.Backup;
using Aperture.Web.Features.Shoots;
using Aperture.Web.Features.Updates;
using Microsoft.EntityFrameworkCore;

namespace Aperture.Web.Features.Dashboard;

public enum AttentionLevel
{
    Info,
    Warning,
    Urgent,
}

/// <summary>
/// Something on the dashboard that needs doing, with a link to where it's done. Items worth a notification on the
/// phone have a <paramref name="PushKey"/>: the same key means the same news, so it's sent only once (see PushReminders).
/// </summary>
public sealed record AttentionItem(AttentionLevel Level, string Icon, string Text, string Detail, string Link, string? PushKey = null);

public static class Attention
{
    /// <summary>How far ahead open prep tasks are flagged.</summary>
    public static readonly TimeSpan PrepHorizon = TimeSpan.FromDays(14);

    /// <summary>How far ahead a delivery date is flagged.</summary>
    public const int DeliveryHorizonDays = 7;

    /// <summary>
    /// Most urgent first: backups, overdue invoices, overdue deliveries, deliveries due soon, shoots to invoice,
    /// shoots to mark as shot, open prep, a new version of Aperture.
    /// Backups are only checked where they're expected (the Mac mini, not a development machine).
    /// Open prep is flagged for shoots within <paramref name="prepHorizon"/> (default <see cref="PrepHorizon"/>).
    /// </summary>
    public static async Task<List<AttentionItem>> ItemsAsync(
        ApertureDbContext db, DateTime now, BackupStatus? backup = null, bool expectBackups = false, Release? update = null,
        TimeSpan? prepHorizon = null)
    {
        var today = DateOnly.FromDateTime(now);
        var horizon = prepHorizon ?? PrepHorizon;
        var items = new List<AttentionItem>();

        if (expectBackups)
        {
            if (backup?.LastSuccess is not { } lastSuccess)
            {
                items.Add(new(AttentionLevel.Urgent, "backup", "No backup has run yet",
                    "Set up the nightly backup on the Mac mini (docs/deployment.md)", "settings", "backup-none"));
            }
            else if (backup.IsStale(new DateTimeOffset(now)))
            {
                items.Add(new(AttentionLevel.Urgent, "backup", $"Last backup: {lastSuccess.LocalDateTime:dd.MM.yyyy HH:mm}",
                    "Is a backup drive connected? See Settings for details.", "settings", $"backup-stale:{lastSuccess:O}"));
            }

            // Per nightly run: a drive that's still missing tomorrow is news again.
            foreach (var (problem, i) in (backup?.Problems ?? []).Select((p, i) => (p, i)))
            {
                items.Add(new(AttentionLevel.Warning, "usb_off", "Backup problem", problem, "settings",
                    $"backup-problem:{backup!.LastRun:O}:{i}"));
            }
        }

        // Booked shoots that were shot or are coming up within the prep horizon, plus every shoot with an issued
        // invoice: invoices sent before the shoot (deposits, advance payment) can be overdue too.
        var shoots = await db.Shoots
            .Where(g => g.Status == ShootStatus.Booked || g.Status == ShootStatus.Shot || g.Status == ShootStatus.Delivered)
            .Where(g => g.End <= now || g.Start <= now + horizon || g.Invoices.Any(i => i.Status == InvoiceStatus.Issued))
            .OrderBy(g => g.Start)
            .ToRowsAsync(db);
        var withInvoice = (await db.Invoices.Where(i => i.Status != InvoiceStatus.Cancelled).Select(i => i.ShootId).ToListAsync()).ToHashSet();

        foreach (var shoot in shoots.Where(g => g.IsPastDue(today)))
        {
            var invoice = shoot.Invoice!;
            items.Add(new(AttentionLevel.Urgent, "assignment_late",
                $"Invoice {invoice.Number} is overdue",
                $"{shoot.Financials.Outstanding} open since {invoice.DueDate:dd.MM.yyyy} · {shoot.Title}",
                $"shoots/{shoot.Id}?tab=invoice", $"invoice-overdue:{invoice.Id}"));
        }

        foreach (var shoot in shoots.Where(g => g.IsDeliveryOverdue(today)))
        {
            items.Add(new(AttentionLevel.Urgent, "photo_library",
                $"Delivery for {shoot.Title} is overdue",
                $"Due {shoot.DeliveryDue:ddd dd.MM.yyyy} · {shoot.ClientName}",
                $"shoots/{shoot.Id}", $"delivery-overdue:{shoot.Id}:{shoot.DeliveryDue:yyyy-MM-dd}"));
        }

        foreach (var shoot in shoots.Where(g => g.Status.AwaitsDelivery() && g.DeliveryDue is { } due
                     && due >= today && due <= today.AddDays(DeliveryHorizonDays)))
        {
            items.Add(new(AttentionLevel.Warning, "photo_library",
                $"Deliver {shoot.Title} by {shoot.DeliveryDue:ddd dd.MM.}",
                $"Shoot on {shoot.Start:ddd dd.MM.yyyy} · {shoot.ClientName}",
                $"shoots/{shoot.Id}", $"delivery-due:{shoot.Id}:{shoot.DeliveryDue:yyyy-MM-dd}"));
        }

        foreach (var shoot in shoots.Where(g => g.End <= now && !withInvoice.Contains(g.Id)
                     && g.PaymentState is PaymentState.Unpaid or PaymentState.PartiallyPaid))
        {
            items.Add(new(AttentionLevel.Warning, "request_quote",
                $"{shoot.Title} has no invoice yet",
                $"Shot {shoot.Start:ddd dd.MM.yyyy} · {shoot.Financials.Outstanding} open",
                $"shoots/{shoot.Id}?tab=invoice"));
        }

        foreach (var shoot in shoots.Where(g => g.End <= now && g.Status == ShootStatus.Booked))
        {
            items.Add(new(AttentionLevel.Info, "task_alt",
                $"Mark {shoot.Title} as shot",
                $"Took place {shoot.Start:ddd dd.MM.yyyy}, still marked Booked",
                $"shoots/{shoot.Id}"));
        }

        var soon = shoots.Where(g => g.Start > now && g.Start <= now + horizon).ToList();
        var soonIds = soon.Select(g => g.Id).ToList();
        var openTasks = await db.ChecklistItems
            .Where(i => soonIds.Contains(i.ShootId) && !i.IsDone)
            .GroupBy(i => i.ShootId)
            .Select(g => new { ShootId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ShootId, g => g.Count);
        foreach (var shoot in soon.Where(g => openTasks.ContainsKey(g.Id)))
        {
            var count = openTasks[shoot.Id];
            items.Add(new(AttentionLevel.Info, "checklist",
                $"{count} prep task{(count == 1 ? "" : "s")} open for {shoot.Title}",
                $"{shoot.Start:ddd dd.MM.yyyy, HH:mm} · {shoot.ClientName}",
                $"shoots/{shoot.Id}?tab=prep", $"prep:{shoot.Id}:{shoot.Start:yyyy-MM-dd}"));
        }

        if (update is not null)
        {
            items.Add(new(AttentionLevel.Info, "system_update_alt", $"Aperture {update.Version.Display} is available",
                "Install it under Settings → Updates", "settings#updates", $"update:{update.Version.Text}"));
        }

        return items;
    }
}
