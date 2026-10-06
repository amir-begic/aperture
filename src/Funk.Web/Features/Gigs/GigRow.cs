using Funk.Web.Data;
using Funk.Web.Data.Entities;
using Funk.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace Funk.Web.Features.Gigs;

/// <summary>What gig tables show; projected in SQL so lists don't load whole entity graphs.</summary>
public sealed record GigRow(
    int Id,
    string Title,
    DateTime Start,
    DateTime End,
    GigStatus Status,
    EventType EventType,
    string ClientName,
    string? ClientPhone,
    string? VenueName,
    string? VenueCity,
    Money? QuotedPrice)
{
    public string Venue => VenueCity is null ? VenueName ?? "" : $"{VenueName}, {VenueCity}";

    /// <summary>Sum of payments; filled in by <see cref="GigRowQueries.ToRowsAsync"/>.</summary>
    public Money Received { get; init; }

    /// <summary>Costs billed on top of the quote; filled in by <see cref="GigRowQueries.ToRowsAsync"/>.</summary>
    public Money BilledCosts { get; init; }

    /// <summary>Only meaningful for gigs that are booked or played.</summary>
    public PaymentState? PaymentState => Status is GigStatus.Confirmed or GigStatus.Done
        ? new GigFinancials(QuotedPrice, Money.Zero, Received, BilledCosts).PaymentState
        : null;
}

public static class GigRowQueries
{
    public static IQueryable<GigRow> ToRows(this IQueryable<Gig> gigs) =>
        gigs.Select(g => new GigRow(
            g.Id,
            g.Title,
            g.Start,
            g.End,
            g.Status,
            g.EventType,
            g.Client.Name,
            g.Client.Phone,
            g.Venue == null ? null : g.Venue.Name,
            g.Venue == null ? null : g.Venue.City,
            g.QuotedPrice));

    /// <summary>
    /// Loads rows including what was received and billed on top per gig (summed in C#, see <see cref="Money"/>),
    /// so the payment state can be shown.
    /// </summary>
    public static async Task<List<GigRow>> ToRowsAsync(this IQueryable<Gig> gigs, FunkDbContext db)
    {
        var rows = await gigs.ToRows().ToListAsync();
        var ids = rows.Select(r => r.Id).ToList();
        var payments = await db.Payments
            .Where(p => ids.Contains(p.GigId))
            .Select(p => new { p.GigId, p.Amount })
            .ToListAsync();
        var received = payments
            .GroupBy(p => p.GigId)
            .ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));

        var billedCosts = await db.Expenses
            .Where(e => e.GigId != null && ids.Contains(e.GigId.Value) && e.BilledToClient != null)
            .Select(e => new { GigId = e.GigId!.Value, Billed = e.BilledToClient!.Value })
            .ToListAsync();
        var billed = billedCosts
            .GroupBy(e => e.GigId)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Billed));

        return rows
            .Select(r => r with { Received = received.GetValueOrDefault(r.Id), BilledCosts = billed.GetValueOrDefault(r.Id) })
            .ToList();
    }
}
