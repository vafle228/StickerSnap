namespace StickerSnap.Core.Packs;

/// <summary>What a pack holds: 512 px stickers or 100×100 custom emoji. Telegram fixes it when the pack is made.</summary>
public enum PackKind
{
    Regular,
    CustomEmoji,
}
