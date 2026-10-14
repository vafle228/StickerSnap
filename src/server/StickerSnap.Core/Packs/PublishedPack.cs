namespace StickerSnap.Core.Packs;

// A pack as the messenger has it: what IStickerPublisher reads and Pack.Reconciled takes.

/// <param name="Name">The pack's link name: cats_by_StickerSnapAppBot</param>
public sealed record PublishedPack(string Name, string Title, PackKind Kind, IReadOnlyList<PublishedSticker> Stickers);

/// <param name="Id">What the messenger calls the sticker by when it is replaced, deleted or downloaded</param>
/// <param name="UniqueId">The same for every bot and over time: what the registry of packs keeps</param>
/// <param name="Emoji">The emoji the sticker answers to, if the messenger says</param>
public sealed record PublishedSticker(string Id, string UniqueId, string? Emoji);
