using System.Globalization;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace Yomia.Core;

public static class ComicArchiveHelper
{
    /// <summary>
    /// Whitelist for manga image file extensions
    /// </summary>
    public static readonly HashSet<string> SupportedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif", ".avif", ".jxl", ".tif", ".tiff"
    };

    /// <summary>
    /// Natural-order string comparer
    /// </summary>
    public static readonly StringComparer NaturalPathComparer = StringComparer.Create(CultureInfo.InvariantCulture,
        CompareOptions.NumericOrdering | CompareOptions.IgnoreCase);

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

        var normalized = entryPath.Trim().Replace('\\', '/');
        var fileName = Path.GetFileName(normalized);
        var ext = Path.GetExtension(normalized);

        // exclude directory
        // must be image file
        // exclude shadow file with '._' prefix
        return !normalized.EndsWith('/')
               && !normalized.Contains("__MACOSX/", StringComparison.OrdinalIgnoreCase)
               && !fileName.StartsWith(".", StringComparison.OrdinalIgnoreCase)
               && SupportedImageExtensions.Contains(ext);
    }
}
