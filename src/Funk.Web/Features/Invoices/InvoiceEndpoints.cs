using Funk.Web.Data;
using Funk.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;

namespace Funk.Web.Features.Invoices;

public static class InvoiceEndpoints
{
    /// <summary>
    /// GET /invoices/{id}/pdf: the archived PDF for issued (and cancelled) invoices,
    /// a freshly rendered preview marked DRAFT for drafts.
    /// </summary>
    public static void MapInvoiceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/invoices/{id:int}/pdf", async (int id, IDbContextFactory<FunkDbContext> dbFactory, InvoiceService invoices) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var invoice = await db.Invoices.AsNoTracking()
                .Select(i => new { i.Id, i.Status, i.PdfId })
                .SingleOrDefaultAsync(i => i.Id == id);

            if (invoice is null)
            {
                return Results.NotFound();
            }

            if (invoice.Status != InvoiceStatus.Draft && invoice.PdfId is { } pdfId)
            {
                return Results.Redirect($"/attachments/{pdfId}");
            }

            var (draft, gig) = await invoices.LoadForDocumentAsync(id);
            var pdf = new InvoiceDocument(draft, gig).GeneratePdf();
            return Results.File(pdf, "application/pdf", fileDownloadName: null);
        });
    }
}
