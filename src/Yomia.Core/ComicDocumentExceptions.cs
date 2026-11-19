namespace Yomia.Core;

/// <summary>
/// Comic document field abstract exception
/// </summary>
/// <param name="message"></param>
/// <param name="inner"></param>
public abstract class ComicDocumentException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// If the document is corrupted or the format is invalid.
/// </summary>
/// <param name="filePath"></param>
/// <param name="message"></param>
/// <param name="inner"></param>
public class ComicDocumentCorruptedException(string filePath, string message, Exception? inner = null)
    : ComicDocumentException($"Document is corrupted or the format is invalid [{filePath}]: {message}", inner)
{
    public string FilePath { get; } = filePath;
}

/// <summary>
/// If no valid comic image found within the document.
/// </summary>
/// <param name="filePath"></param>
public class ComicDocumentEmptyException(string filePath) : ComicDocumentException($"Empty document: {filePath}")
{
    public string FilePath { get; } = filePath;
}

/// <summary>
/// If the document is encrypted.
/// </summary>
/// <param name="filePath"></param>
public class ComicDocumentPasswordRequiredException(string filePath)
    : ComicDocumentException($"Password is required [{filePath}]")
{
    public string FilePath { get; } = filePath;
}

/// <summary>
/// If the provided password is incorrect.
/// </summary>
/// <param name="filePath"></param>
public class ComicDocumentIncorrectPasswordException(string filePath)
    : ComicDocumentException($"Password is incorrect [{filePath}]")
{
    public string FilePath { get; } = filePath;
}

/// <summary>
/// If the document or a single page exceeds safety limits.
/// </summary>
/// <param name="filePath"></param>
/// <param name="message"></param>
public class ComicDocumentLimitExceededException(string filePath, string message) :
    ComicDocumentException($"Safety limit exceeded [{filePath}]: {message}")
{
    public string FilePath { get; } = filePath;
}
