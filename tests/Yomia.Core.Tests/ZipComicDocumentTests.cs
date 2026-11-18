using System.IO.Compression;

namespace Yomia.Core.Tests;

public class ZipComicDocumentTests : IDisposable
{
    private readonly string _corruptedZipPath;
    private readonly string _emptyZipPath;
    private readonly string _tempDir;
    private readonly string _validZipPath;

    public ZipComicDocumentTests()
    {
        // Create temp dir for tests
        _tempDir = Path.Combine(Path.GetTempPath(), $"yomia_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _validZipPath = Path.Combine(_tempDir, "valid.cbz");
        _emptyZipPath = Path.Combine(_tempDir, "empty.cbz");
        _corruptedZipPath = Path.Combine(_tempDir, "corrupted.cbz");

        // Create normal cbz
        using (var fs = new FileStream(_validZipPath, FileMode.Create))
        using (var za = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            AddEntry(za, "02.jpg", [0x02, 0xAA]);
            AddEntry(za, "10.jpg", [0x10, 0xBB]);
            AddEntry(za, "01.png", [0x01, 0xCC]);
            AddEntry(za, "sub/", []); // directory
            AddEntry(za, "readme.txt", [0xFF]); // non-image
            AddEntry(za, "corrupted.jpg", []); // 0 byte bad image
        }

        // Create empty cbz
        using (var fs = new FileStream(_emptyZipPath, FileMode.Create))
        using (var za = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            AddEntry(za, "notice.txt", [0x01]);
        }

        // Create corrupted cbz
        File.WriteAllBytes(_corruptedZipPath, [0x00, 0x11, 0x22, 0x33, 0x44]);
    }

    public void Dispose()
    {
        // Automatically delete the temp dir after tests done.
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // ignored
            }
        }
    }

    private static void AddEntry(ZipArchive za, string name, byte[] content)
    {
        var entry = za.CreateEntry(name);
        using var stream = entry.Open();
        stream.Write(content);
    }

    [Fact]
    public void Constructor_ValidArchive_ShouldFilterAndSortPagesCorrectly()
    {
        using var doc = new ZipComicDocument(_validZipPath);
        // After excluding directories, .txt files, and 0-byte files, the number of valid pages is 3.
        Assert.Equal(3, doc.PageCount);
    }

    [Fact]
    public async Task OpenPageStreamAsync_ShouldReturnExactBytesAndBeSeekable()
    {
        using var doc = new ZipComicDocument(_validZipPath);
        // Page 0 should be naturally sorted as "01.png"
        // which contains 0x01 0xCC
        await using var stream = await doc.OpenPageStreamAsync(0);
        Assert.True(stream.CanSeek);
        Assert.Equal(0, stream.Position);
        Assert.Equal(2, stream.Length);
        var buffer = new byte[2];
        var read = await stream.ReadAsync(buffer);
        Assert.Equal(2, read);
        Assert.Equal(0x01, buffer[0]);
        Assert.Equal(0xCC, buffer[1]);
    }

    [Fact]
    public async Task OpenPageStreamAsync_OutOfBounds_ShouldThrow()
    {
        using var doc = new ZipComicDocument(_validZipPath);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => doc.OpenPageStreamAsync(-1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => doc.OpenPageStreamAsync(3));
    }

    [Fact]
    public void Constructor_EmptyArchive_ShouldThrowComicDocumentEmptyException()
    {
        Assert.Throws<ComicDocumentEmptyException>(() => new ZipComicDocument(_emptyZipPath));
    }

    [Fact]
    public void Constructor_CorruptedArchive_ShouldThrowComicDocumentCorruptedException()
    {
        Assert.Throws<ComicDocumentCorruptedException>(() => new ZipComicDocument(_corruptedZipPath));
    }

    [Fact]
    public async Task Dispose_ShouldPreventFurtherReadsAndReleaseFileLock()
    {
        var doc = new ZipComicDocument(_validZipPath);
        doc.Dispose();
        // Verify that reading again after disposal throws an ObjectDisposedException
        await Assert.ThrowsAsync<ObjectDisposedException>(() => doc.OpenPageStreamAsync(0));
        // Verify that the underlying physical files was already disposed
        // which allows open in read-write mode.
        await using var exclusiveStream =
            new FileStream(_validZipPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.NotNull(exclusiveStream);
    }

    [Fact]
    public async Task OpenPageStreamAsync_CancelledToken_ShouldThrowOperationCanceledException()
    {
        using var doc = new ZipComicDocument(_validZipPath);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync(); // pass through cancellation token
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => doc.OpenPageStreamAsync(0, cts.Token));
        // Verify that the lock is not deadlocked which remains possible to read using a valid token
        // even after cancellation.
        await using var normalStream = await doc.OpenPageStreamAsync(0, CancellationToken.None);
        Assert.True(normalStream.Length > 0);
    }
}
