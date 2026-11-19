using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.SevenZip;

namespace Yomia.Core.Tests;

public class SharpCompressArchiveDocumentTests
{
    private const string FixturePassword = "123456";

    private static readonly byte[] ExpectedPage1Bytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x02, 0xAA, 0xBB, 0xFF, 0xD9];
    private static readonly byte[] ExpectedPage2Bytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x10, 0xCC, 0xDD, 0xFF, 0xD9];

    private static string GetFixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    private static byte[] CreateSevenZipBytes(params (string Name, byte[] Content)[] entries)
    {
        using var ms = new MemoryStream();
        var options = new SevenZipWriterOptions(CompressionType.LZMA) { LeaveStreamOpen = true };
        using (var writer = new SevenZipWriter(ms, options))
        {
            foreach (var (name, content) in entries)
            {
                using var entryStream = new MemoryStream(content);
                writer.Write(name, entryStream, null);
            }
        }

        return ms.ToArray();
    }

    private static async Task<byte[]> ReadAllBytesAsync(IComicDocument doc, int pageIndex)
    {
        await using var stream = await doc.OpenPageStreamAsync(pageIndex);
        Assert.True(stream.CanSeek);
        Assert.Equal(0, stream.Position);
        var buffer = new byte[stream.Length];
        await stream.ReadExactlyAsync(buffer);
        return buffer;
    }

    [Fact]
    public async Task Constructor_SampleRar_ShouldFilterSortAndReadExactBytes()
    {
        using var doc = new SharpCompressArchiveDocument(GetFixturePath("sample.cbr"));

        Assert.False(doc.IsSolid);
        Assert.Equal(3, doc.PageCount);

        var page0 = await ReadAllBytesAsync(doc, 0);
        Assert.Equal(67, page0.Length);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], page0[..4]);

        Assert.Equal(ExpectedPage1Bytes, await ReadAllBytesAsync(doc, 1));
        Assert.Equal(ExpectedPage2Bytes, await ReadAllBytesAsync(doc, 2));
    }

    [Fact]
    public async Task OpenPageStreamAsync_SolidRar_ShouldReadAllPagesInRandomOrder()
    {
        using var doc = new SharpCompressArchiveDocument(GetFixturePath("solid.cbr"));

        Assert.True(doc.IsSolid);
        Assert.Equal(3, doc.PageCount);

        // Read out of archive order (page 2 -> page 0 -> page 1) to verify solid RAR sequential fallback.
        Assert.Equal(ExpectedPage2Bytes, await ReadAllBytesAsync(doc, 2));

        var page0 = await ReadAllBytesAsync(doc, 0);
        Assert.Equal(67, page0.Length);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], page0[..4]);

        Assert.Equal(ExpectedPage1Bytes, await ReadAllBytesAsync(doc, 1));
    }

    [Theory]
    [InlineData("encrypted-content.cbr")]
    [InlineData("encrypted-header.cbr")]
    [InlineData("encrypted-zip.cbz")]
    [InlineData("encrypted.cbz")]
    public void Constructor_EncryptedFixtureWithoutPassword_ShouldThrowPasswordRequired(string fixtureName)
    {
        Assert.Throws<ComicDocumentPasswordRequiredException>(() =>
            new SharpCompressArchiveDocument(GetFixturePath(fixtureName)));
    }

    [Theory]
    [InlineData("encrypted-content.cbr")]
    [InlineData("encrypted-header.cbr")]
    [InlineData("encrypted-zip.cbz")]
    [InlineData("encrypted.cbz")]
    public void Constructor_EncryptedFixtureWithWrongPassword_ShouldThrowIncorrectPassword(string fixtureName)
    {
        Assert.Throws<ComicDocumentIncorrectPasswordException>(() =>
            new SharpCompressArchiveDocument(GetFixturePath(fixtureName), "wrong-password"));
    }

    [Theory]
    [InlineData("encrypted-content.cbr", false)]
    [InlineData("encrypted-header.cbr", false)]
    [InlineData("encrypted-zip.cbz", false)]
    [InlineData("encrypted.cbz", true)]
    public async Task Constructor_EncryptedFixtureWithCorrectPassword_ShouldReadAllPages(
        string fixtureName,
        bool expectedSolid)
    {
        using var doc = new SharpCompressArchiveDocument(GetFixturePath(fixtureName), FixturePassword);

        Assert.Equal(expectedSolid, doc.IsSolid);
        Assert.Equal(3, doc.PageCount);

        var page0 = await ReadAllBytesAsync(doc, 0);
        Assert.Equal(67, page0.Length);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], page0[..4]);

        Assert.Equal(ExpectedPage1Bytes, await ReadAllBytesAsync(doc, 1));
        Assert.Equal(ExpectedPage2Bytes, await ReadAllBytesAsync(doc, 2));
    }

    [Fact]
    public async Task Constructor_UnencryptedSevenZip_ShouldFilterSortAndReadCorrectly()
    {
        var sevenZipBytes = CreateSevenZipBytes(
            ("02.jpg", ExpectedPage1Bytes),
            ("10.jpg", ExpectedPage2Bytes),
            ("01.png", [0x89, 0x50, 0x4E, 0x47]),
            ("readme.txt", [0x01, 0x02]));

        using var doc = new SharpCompressArchiveDocument(new TrackingStream(sevenZipBytes), "memory.cb7");

        Assert.Equal(3, doc.PageCount);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], await ReadAllBytesAsync(doc, 0));
        Assert.Equal(ExpectedPage1Bytes, await ReadAllBytesAsync(doc, 1));
        Assert.Equal(ExpectedPage2Bytes, await ReadAllBytesAsync(doc, 2));
    }

    [Fact]
    public async Task OpenPageStreamAsync_OutOfBounds_ShouldThrow()
    {
        using var doc = new SharpCompressArchiveDocument(GetFixturePath("sample.cbr"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => doc.OpenPageStreamAsync(-1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => doc.OpenPageStreamAsync(3));
    }

    [Theory]
    [InlineData("sample.cbr")]
    [InlineData("solid.cbr")]
    public async Task Dispose_WithTrackingStream_ShouldCloseStreamAndKeepReturnedPageStreamReadable(string fixtureName)
    {
        var archiveBytes = File.ReadAllBytes(GetFixturePath(fixtureName));
        var trackingStream = new TrackingStream(archiveBytes);

        var doc = new SharpCompressArchiveDocument(trackingStream, fixtureName);
        await using var pageStream = await doc.OpenPageStreamAsync(1);

        doc.Dispose();
        doc.Dispose(); // verify idempotency

        Assert.True(trackingStream.IsDisposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => doc.OpenPageStreamAsync(0));

        Assert.True(pageStream.CanSeek);
        var buf = new byte[pageStream.Length];
        await pageStream.ReadExactlyAsync(buf);
        Assert.Equal(ExpectedPage1Bytes, buf);
    }

    [Fact]
    public void Constructor_FailurePaths_ShouldCloseUnderlyingStream()
    {
        var empty7zBytes = CreateSevenZipBytes(("readme.txt", [0x01]));
        var emptyStream = new TrackingStream(empty7zBytes);
        Assert.Throws<ComicDocumentEmptyException>(() => new SharpCompressArchiveDocument(emptyStream, "empty.cb7"));
        Assert.True(emptyStream.IsDisposed);

        var corruptedStream = new TrackingStream([0x00, 0x11, 0x22, 0x33, 0x44, 0x55]);
        Assert.Throws<ComicDocumentCorruptedException>(() =>
            new SharpCompressArchiveDocument(corruptedStream, "corrupted.cbr"));
        Assert.True(corruptedStream.IsDisposed);

        // sample.cbr contains 4 entries (01.png, 02.jpg, 10.jpg, readme.txt)
        var overEntriesStream = new TrackingStream(File.ReadAllBytes(GetFixturePath("sample.cbr")));
        Assert.Throws<ComicDocumentLimitExceededException>(() =>
            new SharpCompressArchiveDocument(overEntriesStream, "sample.cbr", maxEntryCount: 3));
        Assert.True(overEntriesStream.IsDisposed);
    }

    [Fact]
    public async Task OpenPageStreamAsync_PageByteLimit_ShouldEnforceExactBoundary()
    {
        var rarBytes = File.ReadAllBytes(GetFixturePath("sample.cbr"));
        using var doc = new SharpCompressArchiveDocument(new TrackingStream(rarBytes), "sample.cbr", maxPageSize: 9);

        Assert.Equal(3, doc.PageCount);

        // Page 1 (02.jpg) is exactly 9 bytes -> allowed
        Assert.Equal(ExpectedPage1Bytes, await ReadAllBytesAsync(doc, 1));

        // Page 0 (01.png) is 67 bytes -> exceeds 9-byte limit
        await Assert.ThrowsAsync<ComicDocumentLimitExceededException>(() => doc.OpenPageStreamAsync(0));
    }

    [Fact]
    public async Task OpenPageStreamAsync_CancelledToken_ShouldThrowAndKeepLockHealthy()
    {
        using var doc = new SharpCompressArchiveDocument(GetFixturePath("sample.cbr"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => doc.OpenPageStreamAsync(0, cts.Token));

        var bytes = await ReadAllBytesAsync(doc, 1);
        Assert.Equal(ExpectedPage1Bytes, bytes);
    }

    private sealed class TrackingStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public bool IsDisposed { get; private set; }
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                IsDisposed = true;
            }

            base.Dispose(disposing);
        }
    }
}
