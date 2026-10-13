namespace StickerSnap.Application.ProjectMedia;

/// <summary>
/// Files made once from a source and kept for the next time they are asked for: the frame strips,
/// covers and playable copies the media engine makes. A key names what was made from which
/// content, so a file never goes stale: a changed source gets a new key. Files nobody asked for
/// in a while are removed.
/// </summary>
public interface IMediaCache
{
    /// <summary>The file kept under <paramref name="key"/>, or null when there is none.</summary>
    /// <param name="key">Lower-case hex, as <see cref="MediaKeys"/> makes it</param>
    /// <param name="extension">Without the dot: `webp`</param>
    string? Find(string key, string extension);

    /// <summary>
    /// The file kept under <paramref name="key"/>, made by <paramref name="write"/> when there is none.
    /// Asked for again while it is being made, it is made once. The making goes on when the caller
    /// stops waiting, for the next one who asks.
    /// </summary>
    /// <param name="write">Writes the file at the path it is given</param>
    /// <exception cref="Core.Errors.AppException">What <paramref name="write"/> throws; save_failed, disk_full</exception>
    Task<string> GetOrAddAsync(
        string key,
        string extension,
        Func<string, CancellationToken, Task> write,
        CancellationToken cancellationToken);
}
