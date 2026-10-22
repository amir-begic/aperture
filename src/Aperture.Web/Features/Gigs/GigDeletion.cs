using Aperture.Web.Data;
using Aperture.Web.Features.Attachments;
using Microsoft.EntityFrameworkCore;

namespace Aperture.Web.Features.Gigs;

public static class GigDeletion
{
    /// <summary>Gigs with costs, payments or invoices are financial records and can only be cancelled.</summary>
    public static Task<bool> HasFinancialRecordsAsync(this ApertureDbContext db, int gigId) =>
        db.Gigs.Where(g => g.Id == gigId).AnyAsync(g => g.Expenses.Any() || g.Payments.Any() || g.Invoices.Any());

    /// <summary>Deletes a gig with its playlists, checklist and files (rows cascade; files are removed here).</summary>
    public static async Task DeleteGigAsync(this ApertureDbContext db, int gigId, AttachmentStore store)
    {
        var attachments = await db.Attachments.AsNoTracking().Where(a => a.GigId == gigId).ToListAsync();
        await db.Gigs.Where(g => g.Id == gigId).ExecuteDeleteAsync();
        attachments.ForEach(store.Delete);
    }
}
