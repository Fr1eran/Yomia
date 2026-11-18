namespace Yomia.Core;

/// <summary>
/// Comic field abstract exception
/// </summary>
/// <param name="message"></param>
/// <param name="inner"></param>
public abstract class ComicDocumentException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// If document is corrupted or the format is invalid.
/// </summary>
/// <param name="filePath"></param>
/// <param name="message"></param>
/// <param name="inner"></param>
public class ComicDocumentCorruptedException(string filePath, string message, Exception? inner = null)
    : ComicDocumentException($"File is corrupted or the format is invalid [{filePath}]: {message}", inner)
{
    public string FilePath { get; } = filePath;
}

/// <summary>
/// If no valid comic image found within the comic document.
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
