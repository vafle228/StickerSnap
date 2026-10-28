namespace StickerSnap.Core.Packs;

// A pack as the messenger has it: what IStickerPublisher reads and Pack.Reconciled takes.

/// <param name="Name">The pack's link name: cats_by_StickerSnapAppBot</param>
public sealed record PublishedPack(string Name, string Title, PackKind Kind, IReadOnlyList<PublishedSticker> Stickers)
{
    /// <summary>
    /// The sticker a send put into the pack, which was <paramref name="before"/> it (null for a pack
    /// it made): the one that was not there. When Telegram gave back a sticker it already had, the
    /// one in the place the send took: <paramref name="position"/>, or the last one.
    /// </summary>
    public PublishedSticker? SentSince(PublishedPack? before, int? position = null)
    {
        var known = before?.Stickers.Select(sticker => sticker.UniqueId).ToHashSet(StringComparer.Ordinal) ?? [];
        var added = Stickers.LastOrDefault(sticker => !known.Contains(sticker.UniqueId));
        if (added is not null) return added;
        return position is { } at && at >= 0 && at < Stickers.Count ? Stickers[at] : Stickers.LastOrDefault();
    }
}

/// <param name="Id">What the messenger calls the sticker by when it is replaced, deleted or downloaded</param>
/// <param name="UniqueId">The same for every bot and over time: what the registry of packs keeps</param>
/// <param name="Emoji">The emoji the sticker answers to, if the messenger says</param>
/// <param name="Format">What its file is</param>
public sealed record PublishedSticker(string Id, string UniqueId, string? Emoji, StickerFormat Format = StickerFormat.Video);
