using System.IO.Compression;

namespace Yomia.Core;

public sealed class ZipComicDocument : IComicDocument
{
    private readonly ZipArchive _archive;
    private readonly List<ZipArchiveEntry> _imageEntries;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _disposed;

    public ZipComicDocument(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);

        var fileStream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            // Open the underlying physical stream in read-only shared mode.
            _archive = new ZipArchive(fileStream, ZipArchiveMode.Read, false);

            // Filter out non-image files, directories, and 0-byte corrupted files.
            _imageEntries = _archive.Entries
                .Where(e => e.Length > 0 && ComicArchiveHelper.IsValidImageEntry(e.FullName))
                .OrderBy(e => e.FullName, ComicArchiveHelper.NaturalPathComparer).ToList();

            if (_imageEntries.Count == 0)
            {
                throw new ComicDocumentEmptyException(FilePath);
            }
        }
        catch (InvalidDataException ex)
        {
            fileStream.Dispose();
            throw new ComicDocumentCorruptedException(FilePath,
                "ZIP archive data is corrupted or in a non-standard format", ex);
        }
        catch
        {
            fileStream.Dispose();
            throw;
        }
    }

    public string FilePath { get; }
    public int PageCount => _imageEntries.Count;

    /// <summary>
    /// Asynchronously open an independent, seekable image stream for a specified page number.
    /// </summary>
    /// <param name="pageIndex"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    /// <exception cref="ObjectDisposedException"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <exception cref="ComicDocumentCorruptedException"></exception>
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
            // Double-check is needed after acquiring the lock
            // to protect against the document being disposed of
            // by an external call while the request is waiting
            // in the queue.
            ObjectDisposedException.ThrowIf(_disposed, this);

            var entry = _imageEntries[pageIndex];
            await using var entryStream = await entry.OpenAsync(ct).ConfigureAwait(false);

            var memoryStream = new MemoryStream((int)entry.Length);
            await entryStream.CopyToAsync(memoryStream, ct).ConfigureAwait(false);

            // Reset the cursor for the caller.
            memoryStream.Position = 0;
            return memoryStream;
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _archive?.Dispose();
    }
}
