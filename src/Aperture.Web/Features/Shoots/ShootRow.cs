using Aperture.Web.Data;
using Aperture.Web.Data.Entities;
using Aperture.Web.Domain;
using Aperture.Web.Features.Invoices;
using Microsoft.EntityFrameworkCore;

namespace Aperture.Web.Features.Shoots;

/// <summary>What shoot tables show; projected in SQL so lists don't load whole entity graphs.</summary>
public sealed record ShootRow(
    int Id,
    string Title,
    DateTime Start,
    DateTime End,
    ShootStatus Status,
    ShootType ShootType,
    string ClientName,
    string? ClientPhone,
    string? LocationName,
    string? LocationCity,
    Money? QuotedPrice,
    DateOnly? DeliveryDue,
    string? GalleryUrl)
{
    public string Location => LocationCity is null ? LocationName ?? "" : $"{LocationName}, {LocationCity}";

    /// <summary>Sum of payments; filled in by <see cref="ShootRowQueries.ToRowsAsync"/>.</summary>
    public Money Received { get; init; }

    /// <summary>Costs billed on top of the quote; filled in by <see cref="ShootRowQueries.ToRowsAsync"/>.</summary>
    public Money BilledCosts { get; init; }

    /// <summary>The issued invoice, if any; filled in by <see cref="ShootRowQueries.ToRowsAsync"/>.</summary>
    public IssuedInvoice? Invoice { get; init; }

    public ShootFinancials Financials => new(QuotedPrice, Money.Zero, Received, BilledCosts, Invoice?.Total);

    /// <summary>Only meaningful for shoots that are booked, shot or delivered.</summary>
    public PaymentState? PaymentState => Status is ShootStatus.Booked or ShootStatus.Shot or ShootStatus.Delivered
        ? Financials.PaymentState
        : null;

    /// <summary>The delivery date has passed and the images aren't marked as delivered.</summary>
    public bool IsDeliveryOverdue(DateOnly today) =>
        Status.AwaitsDelivery() && DeliveryDue is { } due && due < today;

    /// <summary>The invoice's due date has passed and it isn't fully paid.</summary>
    public bool IsPastDue(DateOnly today) =>
        Invoice is not null && Invoice.DueDate < today && PaymentState is Domain.PaymentState.Unpaid or Domain.PaymentState.PartiallyPaid;
}

public static class ShootRowQueries
{
    public static IQueryable<ShootRow> ToRows(this IQueryable<Shoot> shoots) =>
        shoots.Select(s => new ShootRow(
            s.Id,
            s.Title,
            s.Start,
            s.End,
            s.Status,
            s.ShootType,
            s.Client.Name,
            s.Client.Phone,
            s.Location == null ? null : s.Location.Name,
            s.Location == null ? null : s.Location.City,
            s.QuotedPrice,
            s.DeliveryDue,
            s.GalleryUrl));

    /// <summary>
    /// Loads rows including what was received and billed on top per shoot (summed in C#, see <see cref="Money"/>),
    /// so the payment state can be shown.
    /// </summary>
    public static async Task<List<ShootRow>> ToRowsAsync(this IQueryable<Shoot> shoots, ApertureDbContext db)
    {
        var rows = await shoots.ToRows().ToListAsync();
        var ids = rows.Select(r => r.Id).ToList();
        var payments = await db.Payments
            .Where(p => ids.Contains(p.ShootId))
            .Select(p => new { p.ShootId, p.Amount })
            .ToListAsync();
        var received = payments
            .GroupBy(p => p.ShootId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

        var billedCosts = await db.Expenses
            .Where(e => e.ShootId != null && ids.Contains(e.ShootId.Value) && e.BilledToClient != null)
            .Select(e => new { ShootId = e.ShootId!.Value, Billed = e.BilledToClient!.Value })
            .ToListAsync();
        var billed = billedCosts
            .GroupBy(e => e.ShootId)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Billed));

        var invoices = await db.IssuedInvoicesAsync(ids);

        return rows
            .Select(r => r with
            {
                Received = received.GetValueOrDefault(r.Id),
                BilledCosts = billed.GetValueOrDefault(r.Id),
                Invoice = invoices.GetValueOrDefault(r.Id),
            })
            .ToList();
    }
}
