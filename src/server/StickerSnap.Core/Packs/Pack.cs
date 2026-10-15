using StickerSnap.Core.Media;
using StickerSnap.Core.Telegram;

namespace StickerSnap.Core.Packs;

/// <summary>
/// A pack as the app's registry knows it: what Telegram said at the last check, and what only the
/// app knows, which project and segment each sticker came from and every emoji it was sent with.
/// </summary>
/// <param name="Name">The whole link name, with the bot's tail</param>
/// <param name="Status">Missing once Telegram has no pack of this name: deleted through @Stickers</param>
/// <param name="CheckedAt">The last check with Telegram; null when there was none</param>
/// <param name="Stickers">In the pack's order</param>
public sealed record Pack(
    string Name,
    string Title,
    PackKind Kind,
    PresenceStatus Status,
    DateTimeOffset? CheckedAt,
    IReadOnlyList<PackSticker> Stickers)
{
    /// <summary>120 stickers or 200 custom emoji.</summary>
    public int Limit => Kind == PackKind.CustomEmoji
        ? TelegramLimits.Current.EmojiPackCapacity
        : TelegramLimits.Current.RegularPackCapacity;

    public int Count => Stickers.Count;

    /// <summary>A pack the registry did not have, as Telegram has it at <paramref name="now"/>.</summary>
    public static Pack Of(PublishedPack published, DateTimeOffset now) =>
        new Pack(published.Name, published.Title, published.Kind, PresenceStatus.Ok, null, []).Reconciled(published, now);

    public PackSticker? Find(string stickerId) => Stickers.FirstOrDefault(sticker => sticker.Id == stickerId);

    /// <summary>
    /// The pack after a check at <paramref name="now"/>: Telegram's title, kind and stickers in its
    /// order; a sticker the app knew keeps its project and its emoji, unless the emoji were changed
    /// in Telegram. Without <paramref name="published"/> the pack is missing and keeps what it had.
    /// </summary>
    public Pack Reconciled(PublishedPack? published, DateTimeOffset now)
    {
        if (published is null) return this with { Status = PresenceStatus.Missing, CheckedAt = now };

        var known = Stickers.DistinctBy(sticker => sticker.Id).ToDictionary(sticker => sticker.Id);
        var stickers = published.Stickers.Select(sticker => known.TryGetValue(sticker.UniqueId, out var kept)
            ? kept with { FileId = sticker.Id, Emoji = EmojiOf(kept.Emoji, sticker.Emoji) }
            : new PackSticker(sticker.UniqueId, sticker.Id, sticker.Emoji is { } emoji ? [emoji] : [], null));

        return new Pack(published.Name, published.Title, published.Kind, PresenceStatus.Ok, now, [.. stickers]);
    }

    /// <summary>
    /// The pack after the app sent <paramref name="stickerId"/> into it: as Telegram has it at
    /// <paramref name="now"/>, with the new sticker's project and every emoji it was sent with.
    /// A pack the registry did not have is added.
    /// </summary>
    public static Pack Sent(
        Pack? known,
        PublishedPack published,
        string stickerId,
        IReadOnlyList<string> emoji,
        StickerOrigin origin,
        DateTimeOffset now)
    {
        var pack = known?.Reconciled(published, now) ?? Of(published, now);
        return pack with
        {
            Stickers = [.. pack.Stickers.Select(sticker =>
                sticker.Id == stickerId ? sticker with { Emoji = emoji, Origin = origin } : sticker)],
        };
    }

    /// <summary>The pack without one of its stickers, once Telegram deleted it.</summary>
    public Pack Without(string stickerId) => this with { Stickers = [.. Stickers.Where(sticker => sticker.Id != stickerId)] };

    /// <summary>Telegram tells only the first emoji: ours stay while it is still their first.</summary>
    private static IReadOnlyList<string> EmojiOf(IReadOnlyList<string> kept, string? told) =>
        told is null || (kept.Count > 0 && kept[0] == told) ? kept : [told];
}

/// <param name="Id">Telegram's file_unique_id: the same for every bot and over time</param>
/// <param name="FileId">What the bot deletes and downloads it by, as of the last check</param>
/// <param name="Emoji">The emoji it answers to, the first one first</param>
/// <param name="Origin">Null for a sticker sent from elsewhere, such as @Stickers</param>
public sealed record PackSticker(string Id, string FileId, IReadOnlyList<string> Emoji, StickerOrigin? Origin);

/// <param name="ProjectName">The project's name when the sticker was sent, for when the project is gone</param>
public sealed record StickerOrigin(Guid ProjectId, string ProjectName, string SegmentId);
