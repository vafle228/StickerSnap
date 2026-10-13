namespace StickerSnap.Application.Files;

/// <summary>
/// The files the server rendered, by the id GET /api/v1/files knows them by: drafts of the exact
/// preview among temporary files, and exported stickers wherever the stickers folder put them. An
/// export's id keeps working after a restart, since the project remembers it.
/// </summary>
public interface IRenderedFiles
{
    /// <summary>
    /// A place for a new draft and the id it will be served by. Drafts are temporary: the SPA
    /// fetches one as soon as its job is done, and those older than an hour are cleared on the way.
    /// </summary>
    RenderedFile NewDraft();

    /// <summary>Gives the exported sticker at <paramref name="path"/> an id of its own.</summary>
    /// <exception cref="Core.Errors.AppException">save_failed or disk_full</exception>
    Task<string> AddAsync(string path, CancellationToken cancellationToken);

    /// <summary>Forgets an id; the file itself stays. Nothing happens for an id it does not know.</summary>
    /// <exception cref="Core.Errors.AppException">save_failed or disk_full</exception>
    Task RemoveAsync(string fileId, CancellationToken cancellationToken);

    /// <summary>The file of <paramref name="fileId"/>; null for an id it does not know, a malformed one, or a file gone.</summary>
    Task<string?> FindAsync(string fileId, CancellationToken cancellationToken);
}

/// <summary>A file to render to, and the id it is served by.</summary>
public sealed record RenderedFile(string Id, string Path);
