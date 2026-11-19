using System.IO.Compression;
using SharpCompress.Common;
using SharpCompress.Writers.Tar;

namespace Yomia.Core.Tests;

public class ComicDocumentFactoryTests : IDisposable
{
    private const string FixturePassword = "123456";

    private static readonly byte[] ExpectedPage1Bytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x02, 0xAA, 0xBB, 0xFF, 0xD9];
    private readonly ComicDocumentFactory _factory = new();

    private readonly string _tempDir;

    public ComicDocumentFactoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"yomia_factory_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
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

    private static string GetFixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    private static byte[] CreateBareTarBytes()
    {
        using var ms = new MemoryStream();
        using (var writer = new TarWriter(ms, new TarWriterOptions(CompressionType.None, finalizeArchiveOnClose: true)
               {
                   LeaveStreamOpen = true
               }))
        {
            using var entryStream = new MemoryStream([0x01, 0x02, 0x03]);
            writer.Write("01.jpg", entryStream, null);
        }

        return ms.ToArray();
    }

    [Fact]
    public async Task OpenAsync_UnencryptedCbz_ShouldUseZipFastPathAndNormalizeFilePath()
    {
        var cbzPath = Path.Combine(_tempDir, "normal.cbz");
        using (var fs = File.Create(cbzPath))
        using (var za = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry = za.CreateEntry("01.jpg");
            using var es = entry.Open();
            es.Write([0xAA, 0xBB]);
        }

        // Construct a relative path containing ".." to verify normalization to Path.GetFullPath.
        var fileName = Path.GetFileName(cbzPath);
        var unnormalizedPath = Path.Combine(_tempDir, "sub", "..", fileName);

        using var doc = await _factory.OpenAsync(unnormalizedPath);

        Assert.IsType<ZipComicDocument>(doc);
        Assert.Equal(Path.GetFullPath(cbzPath), doc.FilePath);
        Assert.Equal(1, doc.PageCount);

        await using var pageStream = await doc.OpenPageStreamAsync(0);
        var buf = new byte[2];
        await pageStream.ReadExactlyAsync(buf);
        Assert.Equal([0xAA, 0xBB], buf);
    }

    [Theory]
    [InlineData("sample.cbr")]
    [InlineData("solid.cbr")]
    public async Task OpenAsync_RarFixtures_ShouldReturnSharpCompressArchiveDocument(string fixtureName)
    {
        using var doc = await _factory.OpenAsync(GetFixturePath(fixtureName));

        var scDoc = Assert.IsType<SharpCompressArchiveDocument>(doc);
        Assert.Equal(3, scDoc.PageCount);

        await using var page1 = await scDoc.OpenPageStreamAsync(1);
        var buf = new byte[page1.Length];
        await page1.ReadExactlyAsync(buf);
        Assert.Equal(ExpectedPage1Bytes, buf);
    }

    [Fact]
    public async Task OpenAsync_RarDisguisedAsCbz_ShouldDetectMagicBytesAndOpen()
    {
        var disguisedPath = Path.Combine(_tempDir, "disguised.cbz");
        File.Copy(GetFixturePath("sample.cbr"), disguisedPath);

        using var doc = await _factory.OpenAsync(disguisedPath);

        Assert.IsType<SharpCompressArchiveDocument>(doc);
        Assert.Equal(3, doc.PageCount);
    }

    [Theory]
    [InlineData("encrypted-content.cbr")]
    [InlineData("encrypted-header.cbr")]
    [InlineData("encrypted-zip.cbz")]
    [InlineData("encrypted.cbz")] // 7z disguised as .cbz
    public async Task OpenAsync_EncryptedFixtures_ShouldEnforcePasswordAndDecrypt(string fixtureName)
    {
        var path = GetFixturePath(fixtureName);

        await Assert.ThrowsAsync<ComicDocumentPasswordRequiredException>(() => _factory.OpenAsync(path));
        await Assert.ThrowsAsync<ComicDocumentIncorrectPasswordException>(() => _factory.OpenAsync(path, "wrong"));

        using var doc = await _factory.OpenAsync(path, FixturePassword);
        Assert.IsType<SharpCompressArchiveDocument>(doc);
        Assert.Equal(3, doc.PageCount);

        await using var page1 = await doc.OpenPageStreamAsync(1);
        var buf = new byte[page1.Length];
        await page1.ReadExactlyAsync(buf);
        Assert.Equal(ExpectedPage1Bytes, buf);
    }

    [Fact]
    public async Task OpenFromStreamAsync_UnsupportedTarAndInvalidStreams_ShouldThrowCorruptedAndDisposeStream()
    {
        var bareTar = new TrackingStream(CreateBareTarBytes());
        await Assert.ThrowsAsync<ComicDocumentCorruptedException>(() =>
            _factory.OpenFromStreamAsync(bareTar, "archive.cbt"));
        Assert.True(bareTar.IsDisposed);

        byte[] tarGzBytes;
        using (var gzMs = new MemoryStream())
        {
            using (var gz = new GZipStream(gzMs, CompressionMode.Compress, leaveOpen: true))
            {
                var rawTar = CreateBareTarBytes();
                gz.Write(rawTar);
            }

            tarGzBytes = gzMs.ToArray();
        }

        var tarGzStream = new TrackingStream(tarGzBytes);
        await Assert.ThrowsAsync<ComicDocumentCorruptedException>(() =>
            _factory.OpenFromStreamAsync(tarGzStream, "archive.tar.gz"));
        Assert.True(tarGzStream.IsDisposed);

        var emptyStream = new TrackingStream([]);
        await Assert.ThrowsAsync<ComicDocumentCorruptedException>(() =>
            _factory.OpenFromStreamAsync(emptyStream, "empty.cbz"));
        Assert.True(emptyStream.IsDisposed);

        var garbageStream = new TrackingStream([0x00, 0x11, 0x22, 0x33, 0x44]);
        await Assert.ThrowsAsync<ComicDocumentCorruptedException>(() =>
            _factory.OpenFromStreamAsync(garbageStream, "garbage.cbz"));
        Assert.True(garbageStream.IsDisposed);
    }

    [Fact]
    public async Task OpenFromStreamAsync_EmptyZip_ShouldThrowEmptyAndDisposeStream()
    {
        byte[] emptyZipBytes;
        using (var ms = new MemoryStream())
        {
            using (var za = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = za.CreateEntry("notice.txt");
                using var es = entry.Open();
                es.Write([0x01]);
            }

            emptyZipBytes = ms.ToArray();
        }

        var trackingStream = new TrackingStream(emptyZipBytes);
        await Assert.ThrowsAsync<ComicDocumentEmptyException>(() =>
            _factory.OpenFromStreamAsync(trackingStream, "empty.cbz"));
        Assert.True(trackingStream.IsDisposed);
    }

    [Fact]
    public async Task OpenAsync_CancelledToken_ShouldThrowOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _factory.OpenAsync(GetFixturePath("sample.cbr"), ct: cts.Token));
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
