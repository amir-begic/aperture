using Aperture.Web.Data;
using Aperture.Web.Data.Entities;
using Aperture.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace Aperture.Web.Features.Invoices;

/// <summary>An issued invoice as far as a shoot's payment state is concerned.</summary>
public sealed record IssuedInvoice(int Id, int ShootId, string Number, Money Total, DateOnly DueDate);

public static class InvoiceQueries
{
    /// <summary>The issued (not cancelled) invoice per shoot. Totals are computed in C# from the lines.</summary>
    public static async Task<Dictionary<int, IssuedInvoice>> IssuedInvoicesAsync(this ApertureDbContext db, IReadOnlyCollection<int> shootIds)
    {
        var invoices = await db.Invoices.AsNoTracking()
            .Include(i => i.Lines)
            .Where(i => shootIds.Contains(i.ShootId) && i.Status == InvoiceStatus.Issued)
            .ToListAsync();

        return invoices.ToDictionary(
            i => i.ShootId,
            i => new IssuedInvoice(i.Id, i.ShootId, i.Number!, i.Totals.Total, i.DueDate));
    }
}
