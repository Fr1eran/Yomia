using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

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

    private static void AddEntry(ZipArchive za, string name, byte[] content,
        CompressionLevel compressionLevel = CompressionLevel.Optimal)
    {
        var entry = za.CreateEntry(name, compressionLevel);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private static byte[] CreateZipBytes(Action<ZipArchive> build)
    {
        using var ms = new MemoryStream();
        using (var za = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            build(za);
        }

        return ms.ToArray();
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

    [Fact]
    public async Task Dispose_WithTrackingStream_ShouldCloseUnderlyingStreamAndKeepReturnedPageStreamReadable()
    {
        var zipBytes = CreateZipBytes(za => AddEntry(za, "01.jpg", [0x11, 0x22, 0x33, 0x44]));
        var trackingStream = new TrackingStream(zipBytes);
        var doc = new ZipComicDocument(trackingStream, "memory.cbz");
        await using var pageStream = await doc.OpenPageStreamAsync(0);
        doc.Dispose();
        doc.Dispose(); // verify idempotency
        Assert.True(trackingStream.IsDisposed);
        Assert.True(pageStream.CanSeek);
        pageStream.Position = 1;
        var buf = new byte[3];
        var read = await pageStream.ReadAsync(buf);
        Assert.Equal(3, read);
        Assert.Equal([0x22, 0x33, 0x44], buf);
    }

    [Fact]
    public void Constructor_FailurePaths_ShouldCloseUnderlyingStream()
    {
        var emptyBytes = CreateZipBytes(za => AddEntry(za, "readme.txt", [0x01]));
        var emptyStream = new TrackingStream(emptyBytes);
        Assert.Throws<ComicDocumentEmptyException>(() => new ZipComicDocument(emptyStream, "empty.cbz"));
        Assert.True(emptyStream.IsDisposed);
        var corruptedStream = new TrackingStream([0x00, 0x11, 0x22, 0x33]);
        Assert.Throws<ComicDocumentCorruptedException>(() => new ZipComicDocument(corruptedStream, "bad.cbz"));
        Assert.True(corruptedStream.IsDisposed);
        var overEntriesBytes = CreateZipBytes(za =>
        {
            AddEntry(za, "01.jpg", [0x01]);
            AddEntry(za, "02.jpg", [0x02]);
            AddEntry(za, "sub/", []);
            AddEntry(za, "readme.txt", [0x03]);
        });
        var overEntriesStream = new TrackingStream(overEntriesBytes);
        Assert.Throws<ComicDocumentLimitExceededException>(() =>
            new ZipComicDocument(overEntriesStream, "many.cbz", maxEntryCount: 3));
        Assert.True(overEntriesStream.IsDisposed);
    }

    [Fact]
    public async Task OpenPageStreamAsync_PageByteLimit_ShouldEnforceExactBoundaryAndKeepOtherPagesReadable()
    {
        var page0 = Enumerable.Repeat((byte)0xAA, 16).ToArray();
        var page1 = Enumerable.Repeat((byte)0xBB, 17).ToArray();
        var zipBytes = CreateZipBytes(za =>
        {
            AddEntry(za, "01.jpg", page0);
            AddEntry(za, "02.jpg", page1);
        });
        using var doc = new ZipComicDocument(new TrackingStream(zipBytes), "limit.cbz", maxPageSize: 16);
        Assert.Equal(2, doc.PageCount);
        await using var okStream = await doc.OpenPageStreamAsync(0);
        Assert.Equal(16, okStream.Length);
        await Assert.ThrowsAsync<ComicDocumentLimitExceededException>(() => doc.OpenPageStreamAsync(1));
    }

    [Fact]
    public async Task OpenPageStreamAsync_CorruptedSinglePagePayload_ShouldThrowCorruptedWhileGoodPageRemainsReadable()
    {
        var goodPayload = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        var badPayload = Enumerable.Range(0, 256).Select(i => (byte)(255 - i)).ToArray();
        var zipBytes = CreateZipBytes(za =>
        {
            AddEntry(za, "01.jpg", goodPayload);
            AddEntry(za, "02.jpg", badPayload);
        });
        // Locate local file header for "02.jpg" and flip bytes in its compressed payload only.
        var nameBytes = Encoding.UTF8.GetBytes("02.jpg");
        var nameIndex = zipBytes.AsSpan().IndexOf(nameBytes);
        Assert.True(nameIndex >= 30);
        var localHeaderOffset = nameIndex - 30;
        Assert.Equal((byte)'P', zipBytes[localHeaderOffset]);
        Assert.Equal((byte)'K', zipBytes[localHeaderOffset + 1]);
        var extraLen = BinaryPrimitives.ReadUInt16LittleEndian(zipBytes.AsSpan(localHeaderOffset + 28, 2));
        var payloadOffset = localHeaderOffset + 30 + nameBytes.Length + extraLen;
        zipBytes[payloadOffset] ^= 0xFF;
        zipBytes[payloadOffset + 1] ^= 0xFF;
        using var doc = new ZipComicDocument(new TrackingStream(zipBytes), "partial_corrupt.cbz");
        Assert.Equal(2, doc.PageCount);
        await using var okStream = await doc.OpenPageStreamAsync(0);
        var actual = new byte[goodPayload.Length];
        await okStream.ReadExactlyAsync(actual);
        Assert.Equal(goodPayload, actual);
        await Assert.ThrowsAsync<ComicDocumentCorruptedException>(() => doc.OpenPageStreamAsync(1));
    }

    [Fact]
    public async Task OpenPageStreamAsync_MidCopyCancellation_ShouldCleanupAndKeepSubsequentReadsWorking()
    {
        var payload = Enumerable.Range(0, 4096).Select(i => (byte)(i & 0xFF)).ToArray();
        var zipBytes = CreateZipBytes(za =>
        {
            AddEntry(za, "01.jpg", payload, CompressionLevel.NoCompression);
            AddEntry(za, "02.jpg", [0x42, 0x43], CompressionLevel.NoCompression);
        });
        using var cts = new CancellationTokenSource();
        var trackingStream = new TrackingStream(zipBytes)
        {
            OnAsyncRead = totalAsyncBytes =>
            {
                // Local header is small; once past it we are actively reading entry payload bytes.
                if (totalAsyncBytes > 64)
                {
                    cts.Cancel();
                }
            }
        };
        using var doc = new ZipComicDocument(trackingStream, "cancel_mid_copy.cbz");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => doc.OpenPageStreamAsync(0, cts.Token));
        trackingStream.OnAsyncRead = null;
        await using var page1Stream = await doc.OpenPageStreamAsync(1, CancellationToken.None);
        var buf = new byte[2];
        await page1Stream.ReadExactlyAsync(buf);
        Assert.Equal([0x42, 0x43], buf);
    }

    [Fact]
    public async Task Constructor_MixedSlashesInArchive_ShouldSortDeterministicallyByNormalizedPath()
    {
        var zipBytes = CreateZipBytes(za =>
        {
            AddEntry(za, @"Vol.01\10.jpg", [0x10]);
            AddEntry(za, "Vol.01/02.jpg", [0x02]);
            AddEntry(za, @"Vol.01\01.jpg", [0x01]);
        });
        using var doc = new ZipComicDocument(new TrackingStream(zipBytes), "mixed_slashes.cbz");
        Assert.Equal(3, doc.PageCount);
        Assert.Equal(0x01, await ReadSingleByteAsync(doc, 0));
        Assert.Equal(0x02, await ReadSingleByteAsync(doc, 1));
        Assert.Equal(0x10, await ReadSingleByteAsync(doc, 2));
    }

    private static async Task<byte> ReadSingleByteAsync(ZipComicDocument doc, int pageIndex)
    {
        await using var stream = await doc.OpenPageStreamAsync(pageIndex);
        var buf = new byte[1];
        await stream.ReadExactlyAsync(buf);
        return buf[0];
    }

    private sealed class TrackingStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);
        private long _asyncBytesRead;
        public bool IsDisposed { get; private set; }
        public Action<long>? OnAsyncRead { get; set; }
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

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Throttle chunk size slightly so OnAsyncRead triggers mid-copy rather than in one shot.
            var slice = buffer.Length > 32 ? buffer[..32] : buffer;
            var read = await _inner.ReadAsync(slice, cancellationToken).ConfigureAwait(false);
            _asyncBytesRead += read;
            OnAsyncRead?.Invoke(_asyncBytesRead);
            return read;
        }

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
