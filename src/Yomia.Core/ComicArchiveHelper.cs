using System.Buffers;
using System.Globalization;

namespace Yomia.Core;

public static class ComicArchiveHelper
{
    public const int DefaultMaxEntryCount = 10_000;
    public const long DefaultMaxPageSize = 64L * 1024 * 1024; // 64 MiB

    private const int CopyBufferSize = 81_920;
    private const long MaxInitialMemoryStreamCapacity = 4L * 1024 * 1024; // 4 MiB

    /// <summary>
    /// Whitelist for manga image file extensions
    /// </summary>
    public static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".avif", ".jxl", ".tif", ".tiff"
    };

    private static readonly StringComparer NaturalIgnoreCaseComparer = StringComparer.Create(
        CultureInfo.InvariantCulture,
        CompareOptions.NumericOrdering | CompareOptions.IgnoreCase);

    /// <summary>
    /// Natural-order string comparer
    /// </summary>
    public static readonly IComparer<string> NaturalPathComparer = Comparer<string>.Create((x, y) =>
    {
        var primary = NaturalIgnoreCaseComparer.Compare(x, y);
        return primary != 0 ? primary : StringComparer.Ordinal.Compare(x, y);
    });

    /// <summary>
    /// Replace the '\\' with '/'
    /// </summary>
    /// <param name="entryPath"></param>
    /// <returns></returns>
    public static string NormalizePath(string entryPath) => entryPath.Trim().Replace('\\', '/');

    /// <summary>
    /// Determine whether the given archive entry is a valid image
    /// </summary>
    /// <returns></returns>
    public static bool IsValidImageEntry(string? entryPath)
    {
        if (string.IsNullOrWhiteSpace(entryPath))
        {
            return false;
        }

        var normalized = NormalizePath(entryPath);
        var fileName = Path.GetFileName(normalized);
        var extension = Path.GetExtension(normalized);

        // exclude directory
        // must be image file
        // exclude shadow file with '._' prefix
        return !normalized.EndsWith('/')
               && !normalized.Contains("__MACOSX/", StringComparison.OrdinalIgnoreCase)
               && !fileName.StartsWith(".", StringComparison.OrdinalIgnoreCase)
               && SupportedImageExtensions.Contains(extension);
    }

    internal static (FileStream Stream, string FilePath) OpenFileStream(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fullPath = Path.GetFullPath(filePath);
        return (new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read), fullPath);
    }

    internal static void ValidateArchiveArguments(Stream stream, string filePath, int maxEntryCount, long maxPageSize)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("Stream must be readable and seekable", nameof(stream));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntryCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPageSize);
    }

    /// <summary>Copies a page into an independent stream without writing beyond the byte limit.</summary>
    internal static async Task<MemoryStream> ReadStreamBoundedAsync(Stream stream, string filePath, int pageIndex,
        long entrySize, long maxPageSize, CancellationToken ct)
    {
        var initialCapacity = (int)Math.Clamp(entrySize, 0L, MaxInitialMemoryStreamCapacity);
        var memoryStream = new MemoryStream(initialCapacity);
        try
        {
            var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
            try
            {
                long totalBytesRead = 0;
                while (true)
                {
                    var remaining = maxPageSize - totalBytesRead;
                    // Read one extra byte when near the limit to distinguish a full page from an oversized one.
                    var maxToRead = remaining < CopyBufferSize ? (int)remaining + 1 : CopyBufferSize;
                    var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, maxToRead), ct).ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        break;
                    }

                    if (bytesRead > remaining)
                    {
                        throw new ComicDocumentLimitExceededException(filePath,
                            $"Page {pageIndex} actual decompressed size exceeds limit ({maxPageSize} bytes).");
                    }

                    totalBytesRead += bytesRead;
                    memoryStream.Write(buffer, 0, bytesRead);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
            }

            memoryStream.Position = 0;
            return memoryStream;
        }
        catch
        {
            memoryStream.Dispose();
            throw;
        }
    }
}
