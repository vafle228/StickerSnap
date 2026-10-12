namespace StickerSnap.Core.Render;

/// <summary>How long the encoder may think about each frame.</summary>
public enum RenderEffort
{
    /// <summary>The quick render the user waits for: a preview.</summary>
    Draft,

    /// <summary>The file that goes to Telegram: as small and as good as the encoder can make it.</summary>
    Final,
}
