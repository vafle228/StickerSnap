namespace StickerSnap.Application.Sources;

/// <summary>
/// A mark of a file's content, kept in a project to tell later whether the file at its source's
/// path is still the same video. Its form is the adapter's own; nothing parses it.
/// </summary>
public interface IFileFingerprint
{
    /// <exception cref="Core.Errors.AppException">source_not_found when the file cannot be read</exception>
    Task<string> OfAsync(string path, CancellationToken cancellationToken);
}
