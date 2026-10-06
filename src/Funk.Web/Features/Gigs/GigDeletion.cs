using Funk.Web.Data;
using Funk.Web.Features.Attachments;
using Microsoft.EntityFrameworkCore;

namespace Funk.Web.Features.Gigs;

public static class GigDeletion
{
    /// <summary>Gigs with costs or payments are financial records and can only be cancelled.</summary>
    public static Task<bool> HasFinancialRecordsAsync(this FunkDbContext db, int gigId) =>
        db.Gigs.Where(g => g.Id == gigId).AnyAsync(g => g.Expenses.Any() || g.Payments.Any());

    /// <summary>Deletes a gig with its playlists, checklist and files (rows cascade; files are removed here).</summary>
    public static async Task DeleteGigAsync(this FunkDbContext db, int gigId, AttachmentStore store)
    {
        var attachments = await db.Attachments.AsNoTracking().Where(a => a.GigId == gigId).ToListAsync();
        await db.Gigs.Where(g => g.Id == gigId).ExecuteDeleteAsync();
        attachments.ForEach(store.Delete);
    }
}
