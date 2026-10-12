namespace StickerSnap.Core.Render;

/// <summary>A rectangle of the source's picture as shown, in whole pixels from its top left corner.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height);
