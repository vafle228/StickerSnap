namespace StickerSnap.Core.Render;

/// <summary>How the encoder spends its bits.</summary>
public abstract record RateControl
{
    private RateControl()
    {
    }

    /// <summary>One pass at a constant quality: the size is whatever the picture needs.</summary>
    /// <param name="Crf">Lower is better and bigger; 0–63 for VP9</param>
    public sealed record ConstantQuality(int Crf) : RateControl
    {
        public override string ToString() => $"crf {Crf}";
    }

    /// <summary>Two passes aiming at an average bitrate, so the size is known before the render.</summary>
    public sealed record TwoPass(int BitsPerSecond) : RateControl
    {
        public override string ToString() => $"2-pass {BitsPerSecond / 1000} kbit/s";
    }
}
