using SharpCompress.Archives;
using SharpCompress.Common;

namespace Yomia.Core;

public sealed class ComicDocumentFactory : IComicDocumentFactory
{
    public Task<IComicDocument> OpenAsync(string filePath, string? password = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ct.ThrowIfCancellationRequested();

        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(filePath);
            var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return OpenFromStream(stream, fullPath, password, ct);
        }, ct);
    }

    internal Task<IComicDocument> OpenFromStreamAsync(Stream stream, string filePath, string? password = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ct.ThrowIfCancellationRequested();

        return Task.Run(() => OpenFromStream(stream, filePath, password, ct), ct);
    }

    private static IComicDocument OpenFromStream(Stream stream, string filePath, string? password, CancellationToken ct)
    {
        var ownershipTransferred = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            if (!ArchiveFactory.IsArchive(stream, out var archiveType) || archiveType is null)
            {
                throw new ComicDocumentCorruptedException(filePath, "Unrecognized or corrupted archive format");
            }

            stream.Position = 0;

            switch (archiveType.Value)
            {
                case ArchiveType.Zip when string.IsNullOrEmpty(password):
                    ownershipTransferred = true;
                    return new ZipComicDocument(stream, filePath);
                case ArchiveType.Zip:
                case ArchiveType.Rar:
                case ArchiveType.SevenZip:
                    ownershipTransferred = true;
                    return new SharpCompressArchiveDocument(stream, filePath, password);

                default:
                    throw new ComicDocumentCorruptedException(filePath,
                        $"Unsupported archive format: {archiveType.Value}");
            }
        }
        catch (Exception ex) when (!ownershipTransferred)
        {
            stream.Dispose();

            if (ex is ComicDocumentException or OperationCanceledException)
            {
                throw;
            }

            if (ex is SharpCompressException or InvalidDataException or EndOfStreamException)
            {
                throw new ComicDocumentCorruptedException(filePath, "Failed to inspect archive format", ex);
            }

            throw;
        }
    }
}
