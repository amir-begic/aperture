using Funk.Web.Data;
using Funk.Web.Data.Entities;
using Funk.Web.Domain;
using Funk.Web.Features.Gigs;
using Microsoft.EntityFrameworkCore;

namespace Funk.Web.Features.Dashboard;

public enum AttentionLevel
{
    Info,
    Warning,
    Urgent,
}

/// <summary>Something on the dashboard that needs doing, with a link to where it's done.</summary>
public sealed record AttentionItem(AttentionLevel Level, string Icon, string Text, string Detail, string Link);

public static class Attention
{
    /// <summary>How far ahead open prep tasks are flagged.</summary>
    public static readonly TimeSpan PrepHorizon = TimeSpan.FromDays(14);

    /// <summary>Most urgent first: overdue invoices, gigs to invoice, gigs to close, open prep.</summary>
    public static async Task<List<AttentionItem>> ItemsAsync(FunkDbContext db, DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        var items = new List<AttentionItem>();

        // Booked gigs that were played or are coming up within the prep horizon, plus every gig with an issued
        // invoice: invoices sent before the event (deposits, advance payment) can be overdue too.
        var gigs = await db.Gigs
            .Where(g => g.Status == GigStatus.Confirmed || g.Status == GigStatus.Done)
            .Where(g => g.End <= now || g.Start <= now + PrepHorizon || g.Invoices.Any(i => i.Status == InvoiceStatus.Issued))
            .OrderBy(g => g.Start)
            .ToRowsAsync(db);
        var withInvoice = (await db.Invoices.Where(i => i.Status != InvoiceStatus.Cancelled).Select(i => i.GigId).ToListAsync()).ToHashSet();

        foreach (var gig in gigs.Where(g => g.IsPastDue(today)))
        {
            var invoice = gig.Invoice!;
            items.Add(new(AttentionLevel.Urgent, "assignment_late",
                $"Invoice {invoice.Number} is overdue",
                $"{gig.Financials.Outstanding} open since {invoice.DueDate:dd.MM.yyyy} · {gig.Title}",
                $"gigs/{gig.Id}?tab=invoice"));
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

        var soon = gigs.Where(g => g.Start > now && g.Start <= now + PrepHorizon).ToList();
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
                $"gigs/{gig.Id}?tab=prep"));
        }

        return items;
    }
}
