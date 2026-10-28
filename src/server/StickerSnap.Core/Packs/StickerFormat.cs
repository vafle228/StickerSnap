namespace StickerSnap.Core.Packs;

/// <summary>
/// What a sticker's file is. The app makes only video stickers; a pack copied from @Stickers or
/// another bot may hold the other two.
/// </summary>
public enum StickerFormat
{
    /// <summary>A .webp picture.</summary>
    Static,

    /// <summary>A .tgs Lottie animation, which the app does not draw.</summary>
    Animated,

    /// <summary>A .webm video.</summary>
    Video,
}
