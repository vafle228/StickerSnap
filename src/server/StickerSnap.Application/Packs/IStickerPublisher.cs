using StickerSnap.Core.Packs;

namespace StickerSnap.Application.Packs;

/// <summary>
/// The packs where people use the stickers: made, filled and read on behalf of the user this
/// computer is linked to, by the one product bot (@StickerSnapAppBot), so their links end in
/// _by_StickerSnapAppBot. Who the user is, and who talks to the messenger, is the adapter's
/// business: the registration in StickerSnap.Infrastructure decides.
/// </summary>
/// <remarks>
/// A failure is an <see cref="Core.Errors.AppException"/>: rate_limited (params.retryAfterSec),
/// network_unavailable, bot_unavailable, account_not_linked, bot_blocked, pack_name_taken,
/// pack_name_invalid, pack_full, pack_not_found, validation_failed, or telegram_error with the
/// messenger's words in its log.
/// </remarks>
public interface IStickerPublisher
{
    /// <summary>
    /// The pack as the messenger has it now; null when the user has no pack of that name: none in
    /// the messenger, or one that is not theirs.
    /// </summary>
    Task<PublishedPack?> GetPackAsync(string name, CancellationToken cancellationToken);

    /// <summary>Whether a new pack cannot take <paramref name="name"/>: a pack of that name exists, whoever made it.</summary>
    /// <param name="name">The full name, with the bot's suffix</param>
    Task<bool> IsNameTakenAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a .webm once, so a retry of <see cref="CreatePackAsync"/> or <see cref="AddStickerAsync"/>
    /// does not upload it again.
    /// </summary>
    /// <exception cref="Core.Errors.AppException">file_not_found when the file is gone</exception>
    Task<UploadedFile> UploadAsync(string path, CancellationToken cancellationToken);

    /// <param name="stickers">At least one, at most 50</param>
    /// <returns>The pack right after it was made</returns>
    Task<PublishedPack> CreatePackAsync(
        string name,
        string title,
        PackKind kind,
        IReadOnlyList<NewSticker> stickers,
        CancellationToken cancellationToken);

    /// <summary>Puts <paramref name="sticker"/> at the end of the pack.</summary>
    /// <returns>The pack right after the change</returns>
    /// <exception cref="Core.Errors.AppException">pack_full when the pack has no room left</exception>
    Task<PublishedPack> AddStickerAsync(string pack, NewSticker sticker, CancellationToken cancellationToken);

    /// <summary>Puts <paramref name="sticker"/> in the place of <paramref name="oldStickerId"/>, keeping its position.</summary>
    /// <returns>The pack right after the change</returns>
    Task<PublishedPack> ReplaceStickerAsync(
        string pack,
        string oldStickerId,
        NewSticker sticker,
        CancellationToken cancellationToken);

    /// <summary>The last sticker takes the pack with it.</summary>
    /// <param name="stickerId">The sticker's <see cref="PublishedSticker.Id"/> as the messenger gave it now</param>
    Task DeleteStickerAsync(string pack, string stickerId, CancellationToken cancellationToken);

    /// <summary>Writes the sticker's file at <paramref name="path"/>, so the SPA gets it from the server and not from the messenger.</summary>
    Task DownloadAsync(string stickerId, string path, CancellationToken cancellationToken);
}

/// <param name="Emoji">One to twenty emoji the sticker answers to</param>
public sealed record NewSticker(UploadedFile File, IReadOnlyList<string> Emoji);

/// <summary>A .webm sent by <see cref="IStickerPublisher.UploadAsync"/>, ready to go into a pack.</summary>
public sealed record UploadedFile(string Id);
