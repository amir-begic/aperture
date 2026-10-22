using Aperture.Web.Data;
using Aperture.Web.Data.Entities;
using Aperture.Web.Domain;
using Aperture.Web.Features.Backup;
using Aperture.Web.Features.Gigs;
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

    /// <summary>
    /// Most urgent first: backups, overdue invoices, gigs to invoice, gigs to close, open prep, a new version of Aperture.
    /// Backups are only checked where they're expected (the Mac mini, not a development machine).
    /// Open prep is flagged for gigs within <paramref name="prepHorizon"/> (default <see cref="PrepHorizon"/>).
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

        // Booked gigs that were played or are coming up within the prep horizon, plus every gig with an issued
        // invoice: invoices sent before the event (deposits, advance payment) can be overdue too.
        var gigs = await db.Gigs
            .Where(g => g.Status == GigStatus.Confirmed || g.Status == GigStatus.Done)
            .Where(g => g.End <= now || g.Start <= now + horizon || g.Invoices.Any(i => i.Status == InvoiceStatus.Issued))
            .OrderBy(g => g.Start)
            .ToRowsAsync(db);
        var withInvoice = (await db.Invoices.Where(i => i.Status != InvoiceStatus.Cancelled).Select(i => i.GigId).ToListAsync()).ToHashSet();

        foreach (var gig in gigs.Where(g => g.IsPastDue(today)))
        {
            var invoice = gig.Invoice!;
            items.Add(new(AttentionLevel.Urgent, "assignment_late",
                $"Invoice {invoice.Number} is overdue",
                $"{gig.Financials.Outstanding} open since {invoice.DueDate:dd.MM.yyyy} · {gig.Title}",
                $"gigs/{gig.Id}?tab=invoice", $"invoice-overdue:{invoice.Id}"));
        }

        foreach (var gig in gigs.Where(g => g.End <= now && !withInvoice.Contains(g.Id)
                     && g.PaymentState is PaymentState.Unpaid or PaymentState.PartiallyPaid))
        {
            items.Add(new(AttentionLevel.Warning, "request_quote",
                $"{gig.Title} has no invoice yet",
                $"Played {gig.Start:ddd dd.MM.yyyy} · {gig.Financials.Outstanding} open",
                $"gigs/{gig.Id}?tab=invoice"));
        }

        foreach (var gig in gigs.Where(g => g.End <= now && g.Status == GigStatus.Confirmed))
        {
            items.Add(new(AttentionLevel.Info, "task_alt",
                $"Mark {gig.Title} as done",
                $"Played {gig.Start:ddd dd.MM.yyyy}, still marked Confirmed",
                $"gigs/{gig.Id}"));
        }

        var soon = gigs.Where(g => g.Start > now && g.Start <= now + horizon).ToList();
        var soonIds = soon.Select(g => g.Id).ToList();
        var openTasks = await db.ChecklistItems
            .Where(i => soonIds.Contains(i.GigId) && !i.IsDone)
            .GroupBy(i => i.GigId)
            .Select(g => new { GigId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.GigId, g => g.Count);
        foreach (var gig in soon.Where(g => openTasks.ContainsKey(g.Id)))
        {
            var count = openTasks[gig.Id];
            items.Add(new(AttentionLevel.Info, "checklist",
                $"{count} prep task{(count == 1 ? "" : "s")} open for {gig.Title}",
                $"{gig.Start:ddd dd.MM.yyyy, HH:mm} · {gig.ClientName}",
                $"gigs/{gig.Id}?tab=prep", $"prep:{gig.Id}:{gig.Start:yyyy-MM-dd}"));
        }

        if (update is not null)
        {
            items.Add(new(AttentionLevel.Info, "system_update_alt", $"Aperture {update.Version.Display} is available",
                "Install it under Settings → Updates", "settings#updates", $"update:{update.Version.Text}"));
        }

        return items;
    }
}
