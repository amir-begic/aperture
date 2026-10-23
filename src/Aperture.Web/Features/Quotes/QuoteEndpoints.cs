using Aperture.Web.Data;
using Aperture.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;

namespace Aperture.Web.Features.Quotes;

public static class QuoteEndpoints
{
    /// <summary>GET /quotes/{id}/pdf: the archived PDF of a sent quote, a freshly rendered preview marked DRAFT for drafts.</summary>
    public static void MapQuoteEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/quotes/{id:int}/pdf", async (int id, IDbContextFactory<ApertureDbContext> dbFactory, QuoteService quotes) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var quote = await db.Quotes.AsNoTracking()
                .Select(q => new { q.Id, q.Status, q.PdfId })
                .SingleOrDefaultAsync(q => q.Id == id);

            if (quote is null)
            {
                return Results.NotFound();
            }

            if (quote.Status != QuoteStatus.Draft && quote.PdfId is { } pdfId)
            {
                return Results.Redirect($"/attachments/{pdfId}");
            }

            var (draft, shoot) = await quotes.LoadForDocumentAsync(id);
            return Results.File(new QuoteDocument(draft, shoot).GeneratePdf(), "application/pdf", fileDownloadName: null);
        });
    }
}
