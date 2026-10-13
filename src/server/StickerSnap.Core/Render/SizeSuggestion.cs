namespace StickerSnap.Core.Render;

/// <summary>A one-click edit that brings a sticker under its size limit (3c G): «Укоротить», «Ускорить».</summary>
public abstract record SizeSuggestion
{
    private SizeSuggestion()
    {
    }

    /// <summary>End the segment at <paramref name="OutFrame"/>; the sticker then plays <paramref name="DurationSec"/>.</summary>
    public sealed record Trim(int OutFrame, double DurationSec) : SizeSuggestion
    {
        public override string ToString() => $"trim to frame {OutFrame} ({DurationSec:0.##} s)";
    }

    /// <summary>Play the segment at <paramref name="Value"/> times its recorded speed.</summary>
    public sealed record Speed(double Value) : SizeSuggestion
    {
        public override string ToString() => $"speed {Value:0.##}×";
    }
}
