using StickerSnap.Core.Errors;

namespace StickerSnap.Application.Sources;

/// <summary>POST /api/v1/uploads: a browser's file comes to the server before a project or the check refers to it.</summary>
public interface IUploadService
{
    /// <summary>
    /// Keeps the file and gives its upload id. The format is told by the content, not by the name;
    /// the file is kept with the extension of that format.
    /// </summary>
    /// <param name="fileName">The name the browser gave; only its last part is used</param>
    /// <exception cref="AppException">
    /// format_unsupported with `params.ext` from the name for content the editor does not open;
    /// bad_request with `fields.file: too_large`; save_failed, disk_full
    /// </exception>
    Task<string> UploadAsync(string fileName, Stream content, CancellationToken cancellationToken);
}
