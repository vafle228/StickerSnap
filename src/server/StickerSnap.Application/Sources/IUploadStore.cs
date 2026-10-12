namespace StickerSnap.Application.Sources;

/// <summary>
/// Where files a browser uploads wait until a project takes them (web mode has no paths). An upload
/// nobody takes is gone after a day; one a project takes stays as long as the server keeps its data.
/// </summary>
public interface IUploadStore
{
    /// <summary>
    /// Writes <paramref name="head"/> and then the rest of <paramref name="content"/> under
    /// <paramref name="fileName"/>, and gives the id the SPA refers to it by. A failure, a file over the
    /// limit or a dropped request leaves nothing behind. Uploads older than a day are cleared on the way.
    /// </summary>
    /// <param name="fileName">A safe name of one file, its extension the format the content was told to be</param>
    /// <param name="head">The first bytes, already read to tell the format</param>
    /// <exception cref="Core.Errors.AppException">
    /// bad_request with `fields.file: too_large` over the limit; save_failed, disk_full
    /// </exception>
    Task<string> SaveAsync(string fileName, ReadOnlyMemory<byte> head, Stream content, CancellationToken cancellationToken);

    /// <summary>The file of an upload nobody has taken yet; null for an id it does not know, or a malformed one.</summary>
    string? Find(string uploadId);

    /// <summary>
    /// Takes the upload for a project: the file moves where uploads are not cleared, and the upload's
    /// id refers to nothing any more.
    /// </summary>
    /// <returns>Where the file is now</returns>
    /// <exception cref="Core.Errors.AppException">source_not_found for an id it does not know; save_failed</exception>
    Task<string> KeepAsync(string uploadId, CancellationToken cancellationToken);
}
