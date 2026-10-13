using StickerSnap.Core.Projects;

namespace StickerSnap.Core.Render;

/// <summary>A one-click edit that brings a sticker under its size limit (3c G): «Укоротить», «Ускорить».</summary>
public abstract record SizeSuggestion
{
    private SizeSuggestion()
    {
    }

    /// <summary>The segment as the editor makes it when the suggestion is clicked.</summary>
    public abstract Segment ApplyTo(Segment segment);

    /// <summary>End the segment at <paramref name="OutFrame"/>; the sticker then plays <paramref name="DurationSec"/>.</summary>
    public sealed record Trim(int OutFrame, double DurationSec) : SizeSuggestion
    {
        /// <summary>The excluded frames past the new end go with it, as the editor drops them.</summary>
        public override Segment ApplyTo(Segment segment) => segment with
        {
            OutFrame = OutFrame,
            ExcludedFrames = [.. segment.ExcludedFrames.Where(frame => frame <= OutFrame)],
        };

        public override string ToString() => $"trim to frame {OutFrame} ({DurationSec:0.##} s)";
    }

    /// <summary>Play the segment at <paramref name="Value"/> times its recorded speed.</summary>
    public sealed record Speed(double Value) : SizeSuggestion
    {
        public override Segment ApplyTo(Segment segment) => segment with { Speed = Value };

        public override string ToString() => $"speed {Value:0.##}×";
    }
}
