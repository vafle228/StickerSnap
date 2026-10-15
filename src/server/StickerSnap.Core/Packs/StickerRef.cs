namespace StickerSnap.Core.Packs;

/// <summary>A sticker in a pack (contract, StickerRef): the pack's whole name and the sticker's file_unique_id.</summary>
public sealed record StickerRef(string Pack, string StickerId);
