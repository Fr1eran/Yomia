using System.IO.Compression;

namespace Yomia.Core;

public sealed class ZipComicDocument : IComicDocument
{
    private readonly ZipArchive _archive;
    private readonly List<ZipArchiveEntry> _imageEntries;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly long _maxPageSize;
    private bool _disposed = false;

    public ZipComicDocument(string filePath) : this(ComicArchiveHelper.OpenFileStream(filePath))
    {
    }

    private ZipComicDocument((FileStream Stream, string FilePath) source) : this(source.Stream, source.FilePath)
    {
    }

    internal ZipComicDocument(Stream stream, string filePath,
        int maxEntryCount = ComicArchiveHelper.DefaultMaxEntryCount,
        long maxPageSize = ComicArchiveHelper.DefaultMaxPageSize)
    {
        FilePath = filePath;
        _maxPageSize = maxPageSize;

        ZipArchive? archive = null;

        try
        {
            ComicArchiveHelper.ValidateArchiveArguments(stream, filePath, maxEntryCount, maxPageSize);
            archive = new ZipArchive(stream, ZipArchiveMode.Read, false);

            // This check rejects oversized archives before filtering, sorting,
            // or keeping the document open.
            if (archive.Entries.Count > maxEntryCount)
            {
                throw new ComicDocumentLimitExceededException(FilePath,
                    $"Archive entry count ({archive.Entries.Count}) exceeds maximum allowed ({maxEntryCount})");
            }

            // Filter out non-image files, directories, and 0-byte corrupted files.
            var validEntries =
                archive.Entries.Where(e => e.Length > 0 && ComicArchiveHelper.IsValidImageEntry(e.FullName))
                    .ToList();

            if (validEntries.Count == 0)
            {
                throw new ComicDocumentEmptyException(FilePath);
            }

            if (validEntries.Any(e => e.IsEncrypted))
            {
                throw new ComicDocumentPasswordRequiredException(FilePath);
            }

            _imageEntries = validEntries.OrderBy(e => ComicArchiveHelper.NormalizePath(e.FullName),
                ComicArchiveHelper.NaturalPathComparer).ToList();

            _archive = archive;
        }
        catch (InvalidDataException ex)
        {
            CleanupOnFailure(archive, stream);
            throw new ComicDocumentCorruptedException(FilePath,
                "ZIP archive data is corrupted or in a non-standard format", ex);
        }
        catch
        {
            CleanupOnFailure(archive, stream);
            throw;
        }
    }

    public string FilePath { get; }
    public int PageCount => _imageEntries.Count;

    /// <summary>
    /// Asynchronously open the page stream for a specific page number on demand.
    /// </summary>
    /// <param name="pageIndex"></param>
    /// <param name="ct"></param>
    /// <returns>A caller-owned, seekable stream at position zero, independent of the document.</returns>
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
            var entry = _imageEntries[pageIndex];
            if (entry.Length > _maxPageSize)
            {
                throw new ComicDocumentLimitExceededException(FilePath,
                    $"Page {pageIndex} declared size ({entry.Length} bytes) exceeds limit ({_maxPageSize} bytes)");
            }

            await using var entryStream = await entry.OpenAsync(ct).ConfigureAwait(false);

            return await ComicArchiveHelper.ReadStreamBoundedAsync(
                    entryStream, FilePath, pageIndex, entry.Length, _maxPageSize, ct)
                .ConfigureAwait(false);
        }
        catch (InvalidDataException ex)
        {
            throw new ComicDocumentCorruptedException(FilePath, $"The data of page {pageIndex} is corrupted", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Releases the archive and its synchronization resources.</summary>
    /// <remarks>Stop submitting reads and await all outstanding read tasks before calling this method.</remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _archive.Dispose();
        _lock.Dispose();
    }

    private void CleanupOnFailure(ZipArchive? archive, Stream stream)
    {
        if (archive is not null)
        {
            archive.Dispose();
        }
        else
        {
            stream?.Dispose();
        }

        _lock.Dispose();
    }
}
