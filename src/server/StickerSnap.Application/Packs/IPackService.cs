using StickerSnap.Application.ProjectMedia;
using StickerSnap.Core.Packs;

namespace StickerSnap.Application.Packs;

/// <summary>/api/v1/packs: «Мои паки» (artboard 7), «Добавить по ссылке» and the name of a new pack.</summary>
public interface IPackService
{
    /// <summary>GET /packs: the registry as it is, without asking Telegram.</summary>
    Task<IReadOnlyList<Pack>> ListAsync(CancellationToken cancellationToken);

    /// <summary>GET /packs/{pack}: the pack with the project each sticker came from.</summary>
    /// <exception cref="Core.Errors.AppException">pack_not_found when the registry has no such pack</exception>
    Task<PackView> GetAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// POST /packs/{pack}/sync: the pack as Telegram has it now, or missing when Telegram has none;
    /// sends packChanged.
    /// </summary>
    /// <exception cref="Core.Errors.AppException">
    /// pack_not_found when the registry has no such pack; rate_limited, network_unavailable,
    /// bot_unavailable, telegram_error from Telegram
    /// </exception>
    Task<PackView> SyncAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// POST /packs/import, «Добавить по ссылке»: a pack of the product bot onto the list, checked
    /// with Telegram; one already there is checked again. Sends packChanged.
    /// </summary>
    /// <exception cref="Core.Errors.AppException">
    /// bad_request for a link without a pack name; not_our_pack for a pack of another bot;
    /// pack_not_found when Telegram has no such pack; what <see cref="SyncAsync"/> throws for Telegram
    /// </exception>
    Task<PackView> ImportAsync(string link, CancellationToken cancellationToken);

    /// <summary>
    /// POST /packs/copy, «Скопировать в StickerSnap»: a pack of anyone (@Stickers, another bot,
    /// an old pack of ours the service does not know) copied into a new pack of the product bot
    /// titled <paramref name="title"/>, and put on the list. The name comes from the title as in
    /// «Новый пак», with the first free variant when it is taken. Sends packChanged.
    /// </summary>
    /// <exception cref="Core.Errors.AppException">
    /// bad_request for a link without a pack name or an empty title; pack_name_taken when no
    /// variant is free; pack_not_found when Telegram has no such pack; what
    /// <see cref="SyncAsync"/> throws for Telegram
    /// </exception>
    Task<PackView> CopyAsync(string link, string title, CancellationToken cancellationToken);

    /// <summary>DELETE /packs/{pack}, «Убрать из списка»: the app forgets it, Telegram keeps it.</summary>
    /// <exception cref="Core.Errors.AppException">pack_not_found</exception>
    Task ForgetAsync(string name, CancellationToken cancellationToken);

    /// <summary>
    /// DELETE /packs/{pack}/stickers/{stickerId}: the sticker out of the pack in Telegram, for
    /// everyone who added it. A sticker already gone is no failure. Sends packChanged.
    /// </summary>
    /// <exception cref="Core.Errors.AppException">pack_not_found, also when Telegram no longer has the pack; what <see cref="SyncAsync"/> throws</exception>
    Task DeleteStickerAsync(string name, string stickerId, CancellationToken cancellationToken);

    /// <summary>
    /// GET /packs/{pack}/stickers/{stickerId}/media: the sticker's .webm, downloaded once through the
    /// server, so the bot's token never reaches the SPA, and kept on disk for the next time.
    /// </summary>
    /// <exception cref="Core.Errors.AppException">pack_not_found, file_not_found for a sticker the pack does not have; network_unavailable</exception>
    Task<MediaFile> StickerMediaAsync(string name, string stickerId, CancellationToken cancellationToken);

    /// <summary>
    /// A publication sent <paramref name="stickerId"/> into the pack, which Telegram now has as
    /// <paramref name="published"/>: the registry keeps it with its project and every emoji it was
    /// sent with, adding a pack the publication made. Sends packChanged.
    /// </summary>
    /// <exception cref="Core.Errors.AppException">save_failed or disk_full</exception>
    Task<Pack> RecordSentAsync(
        PublishedPack published,
        string stickerId,
        IReadOnlyList<string> emoji,
        StickerOrigin origin,
        CancellationToken cancellationToken);

    /// <summary>
    /// POST /packs/name-check: the link of a new pack from its title, or <paramref name="name"/> as
    /// the user typed it, with what Telegram would refuse and a free variant when it is taken.
    /// </summary>
    /// <exception cref="Core.Errors.AppException">what <see cref="SyncAsync"/> throws for Telegram</exception>
    Task<PackNameCheck> CheckNameAsync(string title, string? name, CancellationToken cancellationToken);
}

/// <summary>A pack with the project of each of its stickers as the project list has it now.</summary>
public sealed record PackView(Pack Pack, IReadOnlyList<PackStickerView> Stickers);

/// <param name="Origin">Null for a sticker sent from elsewhere</param>
public sealed record PackStickerView(PackSticker Sticker, OriginView? Origin);

/// <param name="ProjectName">Its name now, or the one it had when the sticker was sent once it is gone</param>
/// <param name="ProjectStatus">Missing when the project is off the list or its file is gone</param>
public sealed record OriginView(Guid ProjectId, string ProjectName, string SegmentId, Core.Media.PresenceStatus ProjectStatus);

/// <param name="Name">The part before the bot's tail</param>
/// <param name="FullName">With the tail</param>
/// <param name="Suggestion">A free variant, when the name is taken</param>
public sealed record PackNameCheck(string Name, string FullName, IReadOnlyList<PackNameProblem> Problems, string? Suggestion);
