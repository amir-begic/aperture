using System.Text;
using Aperture.Web.Configuration;
using Aperture.Web.Data.Entities;
using Aperture.Web.Features.Attachments;

namespace Aperture.Tests.Features;

public sealed class AttachmentStoreTests : IDisposable
{
    private readonly string _dataDirectory = Directory.CreateTempSubdirectory("aperture-tests-").FullName;
    private readonly AttachmentStore _store;

    public AttachmentStoreTests()
    {
        _store = new AttachmentStore(new AperturePaths(_dataDirectory), new FixedTimeProvider(new DateTime(2026, 12, 12, 23, 30, 0)));
    }

    public void Dispose() => Directory.Delete(_dataDirectory, recursive: true);

    private static MemoryStream Content(string text = "%PDF-1.7 test") => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task Saves_under_the_year_with_a_generated_name()
    {
        var attachment = await _store.SaveAsync(Content(), "Rechnung Licht.PDF", shootId: 7);

        Assert.Equal("Rechnung Licht.PDF", attachment.FileName);
        Assert.Equal("application/pdf", attachment.ContentType);
        Assert.Equal(7, attachment.ShootId);
        Assert.Equal(13, attachment.SizeBytes);
        Assert.StartsWith("2026" + Path.DirectorySeparatorChar, attachment.StoragePath);
        Assert.EndsWith(".pdf", attachment.StoragePath);
        Assert.DoesNotContain("Rechnung", attachment.StoragePath);
        Assert.True(File.Exists(Path.Combine(_dataDirectory, "attachments", attachment.StoragePath)));
    }

    [Theory]
    [InlineData("page.html")]
    [InlineData("drawing.svg")]
    [InlineData("script.js")]
    [InlineData("no-extension")]
    public async Task Rejects_files_a_browser_could_run(string fileName)
    {
        await Assert.ThrowsAsync<AttachmentException>(() => _store.SaveAsync(Content(), fileName, shootId: null));
        Assert.False(Directory.Exists(Path.Combine(_dataDirectory, "attachments")));
    }

    [Fact]
    public async Task Content_type_comes_from_the_extension_not_the_uploader()
    {
        var attachment = await _store.SaveAsync(Content(), "receipt.jpg", shootId: null);

        Assert.Equal("image/jpeg", attachment.ContentType);
    }

    [Fact]
    public async Task Original_name_is_reduced_to_a_file_name()
    {
        var attachment = await _store.SaveAsync(Content(), "../../etc/receipt.pdf", shootId: null);

        Assert.Equal("receipt.pdf", attachment.FileName);
    }

    [Fact]
    public void Refuses_storage_paths_outside_the_attachments_directory()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _store.GetFullPath(new Attachment { StoragePath = "../aperture.db" }));
    }

    [Fact]
    public async Task Commit_removes_the_new_file_when_saving_fails()
    {
        var added = await _store.SaveAsync(Content(), "receipt.pdf", shootId: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _store.CommitAsync(() => throw new InvalidOperationException("db down"), added, removed: null));

        Assert.False(File.Exists(_store.GetFullPath(added)));
    }

    [Fact]
    public async Task Commit_removes_the_replaced_file_when_saving_succeeds()
    {
        var old = await _store.SaveAsync(Content(), "old.pdf", shootId: null);
        var added = await _store.SaveAsync(Content(), "new.pdf", shootId: null);

        await _store.CommitAsync(() => Task.CompletedTask, added, removed: old);

        Assert.False(File.Exists(_store.GetFullPath(old)));
        Assert.True(File.Exists(_store.GetFullPath(added)));
    }

    [Theory]
    [InlineData("application/pdf", true)]
    [InlineData("image/png", true)]
    [InlineData("image/heic", false)]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", false)]
    public void Only_pdfs_and_common_images_open_in_the_browser(string contentType, bool expected)
    {
        Assert.Equal(expected, AttachmentStore.CanShowInBrowser(contentType));
    }
}
