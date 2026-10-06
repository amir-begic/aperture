using Funk.Web.Data;
using Funk.Web.Data.Entities;
using Funk.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace Funk.Web.Features.Invoices;

/// <summary>An issued invoice as far as a gig's payment state is concerned.</summary>
public sealed record IssuedInvoice(int Id, int GigId, string Number, Money Total, DateOnly DueDate);

public static class InvoiceQueries
{
    /// <summary>The issued (not cancelled) invoice per gig. Totals are computed in C# from the lines.</summary>
    public static async Task<Dictionary<int, IssuedInvoice>> IssuedInvoicesAsync(this FunkDbContext db, IReadOnlyCollection<int> gigIds)
    {
        var invoices = await db.Invoices.AsNoTracking()
            .Include(i => i.Lines)
            .Where(i => gigIds.Contains(i.GigId) && i.Status == InvoiceStatus.Issued)
            .ToListAsync();

        return invoices.ToDictionary(
            i => i.GigId,
            i => new IssuedInvoice(i.Id, i.GigId, i.Number!, i.Totals.Total, i.DueDate));
    }
}
