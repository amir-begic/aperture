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
/// Something on the dashboard that needs doing, with a link to where it's done. Items worth a notification
/// have a <paramref name="PushKey"/>: the same key means the same news, so it's sent only once (see PushReminders).
/// </summary>
public sealed record AttentionItem(AttentionLevel Level, string Icon, string Text, string Detail, string Link, string? PushKey = null);

public static class Attention
{
    /// <summary>How far ahead open prep tasks are flagged.</summary>
    public static readonly TimeSpan PrepHorizon = TimeSpan.FromDays(14);

    /// <summary>How far ahead a delivery date is flagged.</summary>
    public const int DeliveryHorizonDays = 7;

    /// <summary>An inquiry this many days old, still without a yes or no, is flagged for a follow-up.</summary>
    public const int InquiryFollowUpDays = 7;

    /// <summary>
    /// Most urgent first: backups, overdue invoices, overdue deliveries, deliveries due soon, inquiries waiting for an
    /// answer, shoots to invoice, shoots to mark as shot, past inquiries to close, open prep, a new version of Aperture.
    /// Backups are only checked where they're expected (an installation, not a development machine).
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
                    "The installer sets it up. Settings → Backups shows the details.", "settings", "backup-none"));
            }
            else if (backup.IsStale(new DateTimeOffset(now)))
            {
                items.Add(new(AttentionLevel.Urgent, "backup", $"Last backup: {lastSuccess.LocalDateTime:dd.MM.yyyy HH:mm}",
                    "Settings → Backups shows where it goes and what went wrong.", "settings", $"backup-stale:{lastSuccess:O}"));
            }

            // Per backup run: a drive that's still missing next time is news again.
            foreach (var (problem, i) in (backup?.Problems ?? []).Select((p, i) => (p, i)))
            {
                items.Add(new(AttentionLevel.Warning, "usb_off", "Backup problem", problem, "settings",
                    $"backup-problem:{backup!.LastRun:O}:{i}"));
            }
        }

        // Booked shoots that were shot or are coming up within the prep horizon, plus every shoot with an issued
        // invoice: invoices sent before the shoot (deposits, advance payment) can be overdue too.
        var shoots = await db.Shoots
            .Where(s => s.Status == ShootStatus.Booked || s.Status == ShootStatus.Shot || s.Status == ShootStatus.Delivered)
            .Where(s => s.End <= now || s.Start <= now + horizon || s.Invoices.Any(i => i.Status == InvoiceStatus.Issued))
            .OrderBy(s => s.Start)
            .ToRowsAsync(db);
        var withInvoice = (await db.Invoices.Where(i => i.Status != InvoiceStatus.Cancelled).Select(i => i.ShootId).ToListAsync()).ToHashSet();

        foreach (var shoot in shoots.Where(s => s.IsPastDue(today)))
        {
            var invoice = shoot.Invoice!;
            items.Add(new(AttentionLevel.Urgent, "assignment_late",
                $"Invoice {invoice.Number} is overdue",
                $"{shoot.Financials.Outstanding} open since {invoice.DueDate:dd.MM.yyyy} · {shoot.Title}",
                $"shoots/{shoot.Id}?tab=invoice", $"invoice-overdue:{invoice.Id}"));
        }

        foreach (var shoot in shoots.Where(s => s.IsDeliveryOverdue(today)))
        {
            items.Add(new(AttentionLevel.Urgent, "photo_library",
                $"Delivery for {shoot.Title} is overdue",
                $"Due {shoot.DeliveryDue:ddd dd.MM.yyyy} · {shoot.ClientName}",
                $"shoots/{shoot.Id}", $"delivery-overdue:{shoot.Id}:{shoot.DeliveryDue:yyyy-MM-dd}"));
        }

        foreach (var shoot in shoots.Where(s => s.Status.AwaitsDelivery() && s.DeliveryDue is { } due
                     && due >= today && due <= today.AddDays(DeliveryHorizonDays)))
        {
            items.Add(new(AttentionLevel.Warning, "photo_library",
                $"Deliver {shoot.Title} by {shoot.DeliveryDue:ddd dd.MM.}",
                $"Shoot on {shoot.Start:ddd dd.MM.yyyy} · {shoot.ClientName}",
                $"shoots/{shoot.Id}", $"delivery-due:{shoot.Id}:{shoot.DeliveryDue:yyyy-MM-dd}"));
        }

        // Inquiries: a date held for a client who hasn't decided. Followed up after a week, closed once the date is past.
        var inquiries = await db.Shoots
            .Where(s => s.Status == ShootStatus.Inquiry)
            .OrderBy(s => s.Start)
            .Select(s => new { s.Id, s.Title, s.Start, s.End, s.CreatedAt, ClientName = s.Client.Name })
            .ToListAsync();
        var followUpBefore = now.AddDays(-InquiryFollowUpDays);
        foreach (var inquiry in inquiries.Where(i => i.End > now && i.CreatedAt <= followUpBefore))
        {
            items.Add(new(AttentionLevel.Warning, "contact_mail",
                $"Follow up on {inquiry.Title}",
                $"Inquiry since {inquiry.CreatedAt:dd.MM.yyyy}, for {inquiry.Start:ddd dd.MM.yyyy} · {inquiry.ClientName}",
                $"shoots/{inquiry.Id}", $"inquiry-waiting:{inquiry.Id}"));
        }

        foreach (var shoot in shoots.Where(s => s.End <= now && !withInvoice.Contains(s.Id)
                     && s.PaymentState is PaymentState.Unpaid or PaymentState.PartiallyPaid))
        {
            items.Add(new(AttentionLevel.Warning, "request_quote",
                $"{shoot.Title} has no invoice yet",
                $"Shot {shoot.Start:ddd dd.MM.yyyy} · {shoot.Financials.Outstanding} open",
                $"shoots/{shoot.Id}?tab=invoice"));
        }

        foreach (var shoot in shoots.Where(s => s.End <= now && s.Status == ShootStatus.Booked))
        {
            items.Add(new(AttentionLevel.Info, "task_alt",
                $"Mark {shoot.Title} as shot",
                $"Took place {shoot.Start:ddd dd.MM.yyyy}, still marked Booked",
                $"shoots/{shoot.Id}"));
        }

        foreach (var inquiry in inquiries.Where(i => i.End <= now))
        {
            items.Add(new(AttentionLevel.Info, "event_busy",
                $"{inquiry.Title} is still an inquiry",
                $"Was on {inquiry.Start:ddd dd.MM.yyyy}: set it to Booked or Cancelled · {inquiry.ClientName}",
                $"shoots/{inquiry.Id}"));
        }

        var soon = shoots.Where(s => s.Start > now && s.Start <= now + horizon).ToList();
        var soonIds = soon.Select(s => s.Id).ToList();
        var openTasks = await db.ChecklistItems
            .Where(i => soonIds.Contains(i.ShootId) && !i.IsDone)
            .GroupBy(i => i.ShootId)
            .Select(g => new { ShootId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.ShootId, g => g.Count);
        foreach (var shoot in soon.Where(s => openTasks.ContainsKey(s.Id)))
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
