namespace Yomia.Core;

/// <summary>
/// General comic document interface
/// </summary>
public interface IComicDocument : IDisposable
{
    /// <summary>
    /// The local absolute physical path to the comic document.
    /// </summary>
    string FilePath { get; }

    /// <summary>
    /// Total effective page count of the comic document
    /// </summary>
    int PageCount { get; }

    /// <summary>
    /// Asynchronously open the page stream for a specific page number on demand.
    /// </summary>
    /// <param name="pageIndex"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    Task<Stream> OpenPageStreamAsync(int pageIndex, CancellationToken ct = default);
}

/// <summary>
/// Comic document creation factory interface
/// </summary>
public interface IComicDocumentFactory
{
    /// <summary>
    /// Identify the format and open the comic document asynchronously.
    /// </summary>
    /// <param name="filePath">physical file path</param>
    /// <param name="password">optional password</param>
    /// <param name="ct"></param>
    /// <returns></returns>
    Task<IComicDocument> OpenAsync(string filePath, string? password = null, CancellationToken ct = default);
}
