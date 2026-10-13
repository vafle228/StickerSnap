namespace StickerSnap.Application.Exports;

/// <summary>
/// The folder exported stickers go to («Папки», stickers). A sticker is rendered to a temporary
/// file there, so a failed export never touches a file of the same name, and then takes its name.
/// </summary>
public interface IStickerFolder
{
    /// <summary>A temporary file in <paramref name="folder"/> to render to; the folder is created when needed.</summary>
    /// <exception cref="Core.Errors.AppException">save_failed or disk_full</exception>
    Task<StickerSlot> ReserveAsync(string folder, CancellationToken cancellationToken);

    /// <summary>
    /// The rendered file takes the first of <paramref name="names"/> that no file has, or that
    /// <paramref name="replacing"/> has: a new export of a segment replaces its own last file and
    /// never another one.
    /// </summary>
    /// <param name="replacing">The file of the segment's last export, if any</param>
    /// <returns>Where the sticker is now</returns>
    /// <exception cref="Core.Errors.AppException">save_failed or disk_full</exception>
    Task<string> KeepAsync(StickerSlot slot, IEnumerable<string> names, string? replacing, CancellationToken cancellationToken);

    /// <summary>Removes the temporary file, if it is there.</summary>
    void Discard(StickerSlot slot);
}

/// <summary>A temporary file in a stickers folder.</summary>
public sealed record StickerSlot(string Folder, string TemporaryPath);
