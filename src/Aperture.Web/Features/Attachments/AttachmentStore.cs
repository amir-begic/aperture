using Aperture.Web.Configuration;
using Aperture.Web.Data.Entities;
using Microsoft.AspNetCore.StaticFiles;

namespace Aperture.Web.Features.Attachments;

/// <summary>A file that can't be stored; the message is shown to the user.</summary>
public sealed class AttachmentException(string message) : Exception(message);

/// <summary>
/// Stores uploaded files on disk under <see cref="AperturePaths.AttachmentsDirectory"/>, one folder per year,
/// with generated names. The original name is kept in the database only.
/// </summary>
public sealed class AttachmentStore(AperturePaths paths, TimeProvider time)
{
    public const long MaxFileSize = 20 * 1024 * 1024;

    /// <summary>Documents and images only: nothing a browser would run as a page or script.</summary>
    public static readonly IReadOnlySet<string> AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".jpg", ".jpeg", ".png", ".webp", ".gif", ".heic",
        ".doc", ".docx", ".xls", ".xlsx", ".csv", ".txt",
    };

    /// <summary>For the file input's accept attribute; image/* lets phones offer the camera.</summary>
    public static readonly string Accept = "image/*," + string.Join(",", AllowedExtensions);

    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    /// <summary>Writes the file to disk and returns its (unsaved) database row.</summary>
    public async Task<Attachment> SaveAsync(Stream content, string fileName, int? shootId, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            throw new AttachmentException($"{fileName}: only PDFs, images and office documents can be uploaded.");
        }

        var now = time.GetLocalNow().DateTime;
        var storagePath = Path.Combine(now.Year.ToString(), $"{Guid.NewGuid():N}{extension}");
        var fullPath = GetFullPath(storagePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        long size;
        await using (var file = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write))
        {
            await content.CopyToAsync(file, cancellationToken);
            size = file.Length;
        }

        return new Attachment
        {
            ShootId = shootId,
            FileName = Path.GetFileName(fileName),
            ContentType = ContentTypes.TryGetContentType(extension, out var type) ? type : "application/octet-stream",
            SizeBytes = size,
            StoragePath = storagePath,
            UploadedAt = now,
        };
    }

    public string GetFullPath(Attachment attachment) => GetFullPath(attachment.StoragePath);

    /// <summary>Removes the file from disk. Missing files are ignored.</summary>
    public void Delete(Attachment attachment) => File.Delete(GetFullPath(attachment));

    /// <summary>
    /// Runs a database save that adds and/or removes attachment rows, keeping disk in sync:
    /// if the save fails the new file is removed again, if it succeeds the replaced one is.
    /// </summary>
    public async Task CommitAsync(Func<Task> save, Attachment? added, Attachment? removed)
    {
        try
        {
            await save();
        }
        catch
        {
            if (added is not null) Delete(added);
            throw;
        }

        if (removed is not null) Delete(removed);
    }

    public static bool CanShowInBrowser(string contentType) =>
        contentType == "application/pdf" || (contentType.StartsWith("image/") && contentType != "image/heic");

    private string GetFullPath(string storagePath)
    {
        var root = Path.GetFullPath(paths.AttachmentsDirectory);
        var fullPath = Path.GetFullPath(Path.Combine(root, storagePath));

        // Storage paths are generated, but never trust a path read from the database.
        if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Attachment path escapes the attachments directory: {storagePath}");
        }

        return fullPath;
    }
}
