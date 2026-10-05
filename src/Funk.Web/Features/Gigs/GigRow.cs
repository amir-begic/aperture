using Funk.Web.Data.Entities;
using Funk.Web.Domain;

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
    string? VenueName,
    string? VenueCity,
    Money? QuotedPrice)
{
    public string Venue => VenueCity is null ? VenueName ?? "" : $"{VenueName}, {VenueCity}";
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
            g.Venue == null ? null : g.Venue.Name,
            g.Venue == null ? null : g.Venue.City,
            g.QuotedPrice));
}
