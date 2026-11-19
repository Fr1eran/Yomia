using System.Diagnostics.CodeAnalysis;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace Yomia.Core;

public sealed class SharpCompressArchiveDocument : IComicDocument
{
    private readonly IArchive _archive;
    private readonly List<ArchivePageEntry> _imageEntries;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly long _maxPageSize;
    private readonly string? _password;
    private readonly Stream _stream;
    private readonly bool _useSequentialReader;
    private bool _disposed;

    public SharpCompressArchiveDocument(string filePath, string? password = null)
        : this(ComicArchiveHelper.OpenFileStream(filePath), password)
    {
    }

    private SharpCompressArchiveDocument((FileStream Stream, string FilePath) source, string? password)
        : this(source.Stream, source.FilePath, password)
    {
    }

    internal SharpCompressArchiveDocument(Stream stream, string filePath, string? password = null,
        int maxEntryCount = ComicArchiveHelper.DefaultMaxEntryCount,
        long maxPageSize = ComicArchiveHelper.DefaultMaxPageSize)
    {
        FilePath = filePath;
        _maxPageSize = maxPageSize;

        IArchive? archive = null;

        var options = new ReaderOptions
        {
            Password = password,
            LeaveStreamOpen = true
        };

        try
        {
            ComicArchiveHelper.ValidateArchiveArguments(stream, filePath, maxEntryCount, maxPageSize);
            archive = ArchiveFactory.OpenArchive(stream, options);
            var useSequentialReader = archive is { Type: ArchiveType.Rar, IsSolid: true };
            var validEntries = CollectValidEntries(archive, maxEntryCount);

            if (validEntries.Count == 0)
            {
                if (archive.IsEncrypted)
                {
                    throw string.IsNullOrEmpty(password)
                        ? new ComicDocumentPasswordRequiredException(FilePath)
                        : new ComicDocumentIncorrectPasswordException(FilePath);
                }

                throw new ComicDocumentEmptyException(FilePath);
            }

            EnsureDecryptionReady(archive, stream, validEntries, password, useSequentialReader);

            // Filter out non-image files, directories, and 0-byte corrupted files.
            _imageEntries = validEntries
                .OrderBy(e => ComicArchiveHelper.NormalizePath(e.Entry.Key!),
                    ComicArchiveHelper.NaturalPathComparer)
                .ToList();

            _stream = stream;
            _archive = archive;
            _password = password;
            _useSequentialReader = useSequentialReader;
        }
        catch (Exception ex)
        {
            CleanupOnFailure(archive, stream);
            if (TryTranslateException(ex, password, out var domainEx))
            {
                throw domainEx;
            }

            throw;
        }
    }

    public bool IsSolid => _archive.IsSolid;

    public string FilePath { get; }
    public int PageCount => _imageEntries.Count;

    /// <summary>
    /// Asynchronously open the page stream for a specific page number on demand.
    /// </summary>
    /// <param name="pageIndex"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    /// <exception cref="ObjectDisposedException"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <exception cref="ComicDocumentLimitExceededException"></exception>
    /// <exception cref="ComicDocumentCorruptedException"></exception>
    /// <exception cref="OperationCanceledException"></exception>
    public async Task<Stream> OpenPageStreamAsync(int pageIndex, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (pageIndex < 0 || pageIndex >= PageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex),
                $"Page index out of range [0..{PageCount - 1}]: {pageIndex}");
        }

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var pageEntry = _imageEntries[pageIndex];
            if (pageEntry.Entry.Size > _maxPageSize)
            {
                throw new ComicDocumentLimitExceededException(FilePath,
                    $"Page {pageIndex} declared size ({pageEntry.Entry.Size} bytes) exceeds limit ({_maxPageSize} bytes)");
            }

            await using var entryStream = _useSequentialReader
                ? OpenEntryStreamSequential(_stream, pageEntry.ArchiveOrdinal, _password, ct)
                : await pageEntry.Entry.OpenEntryStreamAsync(ct).ConfigureAwait(false);

            return await ComicArchiveHelper.ReadStreamBoundedAsync(
                    entryStream, FilePath, pageIndex, pageEntry.Entry.Size, _maxPageSize, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SharpCompressException or InvalidDataException or EndOfStreamException)
        {
            throw new ComicDocumentCorruptedException(FilePath, $"The data of page {pageIndex} is corrupted", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _archive.Dispose();
        _stream.Dispose();
        _lock.Dispose();
    }

    private List<ArchivePageEntry> CollectValidEntries(IArchive archive, int maxEntryCount)
    {
        if (archive.Volumes.Skip(1).Any())
        {
            throw new ComicDocumentCorruptedException(FilePath, "Multi-volume split archives are not supported");
        }

        var totalEntryCount = 0;
        var validEntries = new List<ArchivePageEntry>();
        foreach (var entry in archive.Entries)
        {
            var currentOrdinal = totalEntryCount;

            // This check rejects oversized archives before filtering, sorting,
            // or keeping the document open.
            if (++totalEntryCount > maxEntryCount)
            {
                throw new ComicDocumentLimitExceededException(FilePath,
                    $"Archive entry count exceeds maximum allowed ({maxEntryCount})");
            }

            // This check rejects multi-volume split archives.
            if (entry.IsSplitAfter)
            {
                throw new ComicDocumentCorruptedException(FilePath,
                    "Multi-volume split archives are not supported");
            }

            if (entry is { IsDirectory: false, Size: > 0 } && ComicArchiveHelper.IsValidImageEntry(entry.Key))
            {
                validEntries.Add(new ArchivePageEntry(entry, currentOrdinal));
            }
        }

        return validEntries;
    }

    private bool TryTranslateException(Exception ex, string? password, [NotNullWhen(true)] out Exception? translated)
    {
        translated = ex switch
        {
            // Let caller throw which preserve the original exception
            // and stack trace.
            ComicDocumentException => null,
            CryptographicException => string.IsNullOrEmpty(password)
                ? new ComicDocumentPasswordRequiredException(FilePath)
                : new ComicDocumentIncorrectPasswordException(FilePath),
            MultipartStreamRequiredException or MultiVolumeExtractionException =>
                new ComicDocumentCorruptedException(FilePath, "Multi-volume archives are not supported", ex),
            SharpCompressException or InvalidDataException or EndOfStreamException =>
                new ComicDocumentCorruptedException(FilePath,
                    "The archive data is corrupted or in a non-standard format", ex),
            _ => null
        };
        return translated is not null;
    }

    private void EnsureDecryptionReady(IArchive archive, Stream stream, List<ArchivePageEntry> validEntries,
        string? password, bool useSequentialReader)
    {
        var encryptedEntry = validEntries.FirstOrDefault(e => e.Entry.IsEncrypted);
        if (!archive.IsEncrypted && encryptedEntry is null)
        {
            // Everything is decrypted, then immediately return.
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            throw new ComicDocumentPasswordRequiredException(FilePath);
        }

        var probeEntry = encryptedEntry ?? validEntries[0];
        try
        {
            using var probeStream = useSequentialReader
                ? OpenEntryStreamSequential(stream, probeEntry.ArchiveOrdinal, password)
                : probeEntry.Entry.OpenEntryStream();
            Span<byte> scratch = stackalloc byte[4096];
            _ = probeStream.Read(scratch);
        }
        catch (Exception ex) when (ex is SharpCompressException or InvalidDataException or EndOfStreamException)
        {
            throw new ComicDocumentIncorrectPasswordException(FilePath);
        }
    }

    private Stream OpenEntryStreamSequential(Stream stream, int targetOrdinal, string? password,
        CancellationToken ct = default)
    {
        stream.Position = 0;
        var reader = ReaderFactory.OpenReader(stream, new ReaderOptions
        {
            Password = password,
            LeaveStreamOpen = true
        });

        try
        {
            var currentOrdinal = 0;
            while (reader.MoveToNextEntry())
            {
                ct.ThrowIfCancellationRequested();
                if (currentOrdinal == targetOrdinal)
                {
                    var entryStream = reader.OpenEntryStream();
                    return new ReaderOwnedStream(reader, entryStream);
                }

                currentOrdinal++;
            }

            throw new ComicDocumentCorruptedException(FilePath, "Target entry was not found in archive stream");
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    private void CleanupOnFailure(IArchive? archive, Stream stream)
    {
        archive?.Dispose();
        stream?.Dispose();
        _lock.Dispose();
    }

    private sealed record ArchivePageEntry(IArchiveEntry Entry, int ArchiveOrdinal);

    private sealed class ReaderOwnedStream(IReader reader, Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                reader.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync().ConfigureAwait(false);
            reader.Dispose();
            await base.DisposeAsync().ConfigureAwait(false);
        }
    }
}
