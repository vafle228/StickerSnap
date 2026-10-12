namespace StickerSnap.Core.Render;

/// <summary>
/// The bitrate that fills a size limit over a duration, for an encoder that aims at a bitrate
/// (two passes): `0.95 · maxBytes · 8 / t`. The 5% left over is the container's: WEBM headers,
/// cues and the slack of the encoder's rate control. A transparent VP9 sticker carries its alpha
/// as a second stream that the encoder drives to the same bitrate (the render spike measured 1.2–1.6
/// times the limit), so each of the two streams gets half.
/// </summary>
public static class TargetBitrate
{
    /// <summary>The share of the limit left to the container.</summary>
    public const double ContainerReserve = 0.05;

    /// <summary>Bits per second of each stream, rounded down: 664 098 for 256 KB over 3 s, half with alpha.</summary>
    public static int BitsPerSecond(int maxBytes, double durationSec, bool transparent = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        if (!(durationSec > 0)) throw new ArgumentOutOfRangeException(nameof(durationSec), durationSec, "must be positive");

        return (int)Math.Floor((1 - ContainerReserve) * maxBytes * 8 / durationSec / (transparent ? 2 : 1));
    }
}
