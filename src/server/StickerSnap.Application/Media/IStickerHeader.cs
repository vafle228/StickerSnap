namespace StickerSnap.Application.Media;

/// <summary>
/// What a rendered sticker file says about itself in its header, apart from its frames: the
/// length Telegram reads, which a long sticker (R19) declares shorter than it plays.
/// </summary>
public interface IStickerHeader
{
    /// <summary>
    /// Makes the sticker at <paramref name="path"/> declare a length of <paramref name="seconds"/>,
    /// in place: its frames, their rate and the file's size stay as they are, so every player still
    /// plays them all.
    /// </summary>
    /// <exception cref="Core.Errors.AppException">
    /// `render_failed` when the file is not a sticker with a declared length to rewrite
    /// </exception>
    Task DeclareDurationAsync(string path, double seconds, CancellationToken cancellationToken);
}
