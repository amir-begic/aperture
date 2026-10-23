using Aperture.Web.Data;
using Aperture.Web.Data.Entities;
using Aperture.Web.Domain;
using Aperture.Web.Features.Backup;
using Aperture.Web.Features.Shoots;
using Aperture.Web.Features.Updates;
using Aperture.Web.Localization;
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
/// The texts are in the current language.
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
                items.Add(new(AttentionLevel.Urgent, "backup", Texts.Get("No backup has run yet"),
                    Texts.Get("The installer sets it up. Settings → Backups shows the details."), "settings", "backup-none"));
            }
            else if (backup.IsStale(new DateTimeOffset(now)))
            {
                items.Add(new(AttentionLevel.Urgent, "backup", Texts.Format("Last backup: {0:dd.MM.yyyy HH:mm}", lastSuccess.LocalDateTime),
                    Texts.Get("Settings → Backups shows where it goes and what went wrong."), "settings", $"backup-stale:{lastSuccess:O}"));
            }

            // Per backup run: a drive that's still missing next time is news again.
            foreach (var (problem, i) in (backup?.Problems ?? []).Select((p, i) => (p, i)))
            {
                items.Add(new(AttentionLevel.Warning, "usb_off", Texts.Get("Backup problem"), problem, "settings",
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
                Texts.Format("Invoice {0} is overdue", invoice.Number),
                Texts.Format("{0} open since {1:dd.MM.yyyy} · {2}", shoot.Financials.Outstanding, invoice.DueDate, shoot.Title),
                $"shoots/{shoot.Id}?tab=invoice", $"invoice-overdue:{invoice.Id}"));
        }

        foreach (var shoot in shoots.Where(s => s.IsDeliveryOverdue(today)))
        {
            items.Add(new(AttentionLevel.Urgent, "photo_library",
                Texts.Format("Delivery for {0} is overdue", shoot.Title),
                Texts.Format("Due {0:ddd dd.MM.yyyy} · {1}", shoot.DeliveryDue, shoot.ClientName),
                $"shoots/{shoot.Id}", $"delivery-overdue:{shoot.Id}:{shoot.DeliveryDue:yyyy-MM-dd}"));
        }

        foreach (var shoot in shoots.Where(s => s.Status.AwaitsDelivery() && s.DeliveryDue is { } due
                     && due >= today && due <= today.AddDays(DeliveryHorizonDays)))
        {
            items.Add(new(AttentionLevel.Warning, "photo_library",
                Texts.Format("Deliver {0} by {1:ddd dd.MM.}", shoot.Title, shoot.DeliveryDue),
                Texts.Format("Shoot on {0:ddd dd.MM.yyyy} · {1}", shoot.Start, shoot.ClientName),
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
                Texts.Format("Follow up on {0}", inquiry.Title),
                Texts.Format("Inquiry since {0:dd.MM.yyyy}, for {1:ddd dd.MM.yyyy} · {2}", inquiry.CreatedAt, inquiry.Start, inquiry.ClientName),
                $"shoots/{inquiry.Id}", $"inquiry-waiting:{inquiry.Id}"));
        }

        foreach (var shoot in shoots.Where(s => s.End <= now && !withInvoice.Contains(s.Id)
                     && s.PaymentState is PaymentState.Unpaid or PaymentState.PartiallyPaid))
        {
            items.Add(new(AttentionLevel.Warning, "request_quote",
                Texts.Format("{0} has no invoice yet", shoot.Title),
                Texts.Format("Shot {0:ddd dd.MM.yyyy} · {1} open", shoot.Start, shoot.Financials.Outstanding),
                $"shoots/{shoot.Id}?tab=invoice"));
        }

        foreach (var shoot in shoots.Where(s => s.End <= now && s.Status == ShootStatus.Booked))
        {
            items.Add(new(AttentionLevel.Info, "task_alt",
                Texts.Format("Mark {0} as shot", shoot.Title),
                Texts.Format("Took place {0:ddd dd.MM.yyyy}, still marked Booked", shoot.Start),
                $"shoots/{shoot.Id}"));
        }

        foreach (var inquiry in inquiries.Where(i => i.End <= now))
        {
            items.Add(new(AttentionLevel.Info, "event_busy",
                Texts.Format("{0} is still an inquiry", inquiry.Title),
                Texts.Format("Was on {0:ddd dd.MM.yyyy}: set it to Booked or Cancelled · {1}", inquiry.Start, inquiry.ClientName),
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
                count == 1 ? Texts.Format("1 prep task open for {0}", shoot.Title) : Texts.Format("{0} prep tasks open for {1}", count, shoot.Title),
                $"{shoot.Start:ddd dd.MM.yyyy, HH:mm} · {shoot.ClientName}",
                $"shoots/{shoot.Id}?tab=prep", $"prep:{shoot.Id}:{shoot.Start:yyyy-MM-dd}"));
        }

        if (update is not null)
        {
            items.Add(new(AttentionLevel.Info, "system_update_alt", Texts.Format("Aperture {0} is available", update.Version.Display),
                Texts.Get("Install it under Settings → Updates"), "settings#updates", $"update:{update.Version.Text}"));
        }

        return items;
    }
}
