using StickerSnap.Core.Packs;

namespace StickerSnap.Application.Packs;

/// <summary>
/// The packs where people use the stickers: made, filled and read on the user's behalf by the
/// one product bot (@StickerSnapAppBot), so their links end in _by_StickerSnapAppBot. Which
/// client talks to the messenger is decided by the registration in StickerSnap.Infrastructure.
/// </summary>
/// <remarks>
/// A failure is an <see cref="Core.Errors.AppException"/>: rate_limited (params.retryAfterSec),
/// network_unavailable, account_not_linked (the user never started the bot), pack_name_taken,
/// pack_full, pack_not_found, or telegram_error with the messenger's words in its log.
/// </remarks>
public interface IStickerPublisher
{
    /// <summary>The pack as the messenger has it now; null when there is no pack of that name.</summary>
    Task<PublishedPack?> GetPackAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a .webm once, so a retry of <see cref="CreatePackAsync"/> or <see cref="AddStickerAsync"/>
    /// does not upload it again.
    /// </summary>
    Task<StickerFile.Uploaded> UploadAsync(long ownerId, string path, CancellationToken cancellationToken);

    /// <param name="ownerId">The user the pack belongs to: who linked the account</param>
    /// <param name="stickers">At least one, at most 50</param>
    Task CreatePackAsync(
        long ownerId,
        string name,
        string title,
        PackKind kind,
        IReadOnlyList<NewSticker> stickers,
        CancellationToken cancellationToken);

    /// <exception cref="Core.Errors.AppException">pack_full when the pack has no room left</exception>
    Task AddStickerAsync(long ownerId, string pack, NewSticker sticker, CancellationToken cancellationToken);

    /// <summary>Puts <paramref name="sticker"/> in the place of <paramref name="oldStickerId"/>, keeping its position.</summary>
    Task ReplaceStickerAsync(
        long ownerId,
        string pack,
        string oldStickerId,
        NewSticker sticker,
        CancellationToken cancellationToken);

    Task DeleteStickerAsync(string stickerId, CancellationToken cancellationToken);

    /// <summary>Writes the sticker's file at <paramref name="path"/>, so the SPA gets it from the server and not from the messenger.</summary>
    Task DownloadAsync(string stickerId, string path, CancellationToken cancellationToken);
}

/// <param name="Emoji">One to twenty emoji the sticker answers to</param>
public sealed record NewSticker(StickerFile File, IReadOnlyList<string> Emoji);

/// <summary>The .webm of a new sticker: a file on disk, or one already sent by <see cref="IStickerPublisher.UploadAsync"/>.</summary>
public abstract record StickerFile
{
    private StickerFile()
    {
    }

    public sealed record Local(string Path) : StickerFile;

    public sealed record Uploaded(string Id) : StickerFile;
}
