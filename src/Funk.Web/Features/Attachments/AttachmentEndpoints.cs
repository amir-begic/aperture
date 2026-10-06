using Funk.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Funk.Web.Features.Attachments;

public static class AttachmentEndpoints
{
    /// <summary>
    /// GET /attachments/{id} shows PDFs and images in the browser and downloads everything else;
    /// ?download=true always downloads. Requires sign-in like every other endpoint.
    /// </summary>
    public static void MapAttachmentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/attachments/{id:int}", async (
            int id, bool? download, HttpContext context,
            IDbContextFactory<FunkDbContext> dbFactory, AttachmentStore store) =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var attachment = await db.Attachments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id);
            var path = attachment is null ? null : store.GetFullPath(attachment);
            if (attachment is null || !File.Exists(path))
            {
                return Results.NotFound();
            }

            var inline = download != true && AttachmentStore.CanShowInBrowser(attachment.ContentType);
            var disposition = new ContentDispositionHeaderValue(inline ? "inline" : "attachment");
            disposition.SetHttpFileName(attachment.FileName);
            context.Response.Headers.ContentDisposition = disposition.ToString();
            context.Response.Headers.XContentTypeOptions = "nosniff";

            return Results.File(path, attachment.ContentType, enableRangeProcessing: true);
        });
    }
}
