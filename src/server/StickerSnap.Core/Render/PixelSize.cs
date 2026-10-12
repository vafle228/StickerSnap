namespace StickerSnap.Core.Render;

/// <summary>A width and a height in whole pixels.</summary>
public readonly record struct PixelSize(int Width, int Height)
{
    public int LongerSide => Math.Max(Width, Height);

    public override string ToString() => $"{Width}x{Height}";
}
