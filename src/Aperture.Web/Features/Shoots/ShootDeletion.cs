using Aperture.Web.Data;
using Aperture.Web.Features.Attachments;
using Microsoft.EntityFrameworkCore;

namespace Aperture.Web.Features.Shoots;

public static class ShootDeletion
{
    /// <summary>Shoots with costs, payments or invoices are financial records and can only be cancelled.</summary>
    public static Task<bool> HasFinancialRecordsAsync(this ApertureDbContext db, int shootId) =>
        db.Shoots.Where(g => g.Id == shootId).AnyAsync(g => g.Expenses.Any() || g.Payments.Any() || g.Invoices.Any());

    /// <summary>Deletes a shoot with its playlists, checklist and files (rows cascade; files are removed here).</summary>
    public static async Task DeleteShootAsync(this ApertureDbContext db, int shootId, AttachmentStore store)
    {
        var attachments = await db.Attachments.AsNoTracking().Where(a => a.ShootId == shootId).ToListAsync();
        await db.Shoots.Where(g => g.Id == shootId).ExecuteDeleteAsync();
        attachments.ForEach(store.Delete);
    }
}
