namespace StickerSnap.Core.Render;

/// <summary>
/// The bitrate that fills a size limit over a duration, for an encoder that aims at a bitrate
/// (two passes): `0.95 · maxBytes · 8 / t`. The 5% left over is the container's: WEBM headers,
/// cues and the slack of the encoder's rate control.
/// </summary>
public static class TargetBitrate
{
    /// <summary>The share of the limit left to the container.</summary>
    public const double ContainerReserve = 0.05;

    /// <summary>Bits per second, rounded down: 664 098 for 256 KB over 3 s.</summary>
    public static int BitsPerSecond(int maxBytes, double durationSec)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        if (!(durationSec > 0)) throw new ArgumentOutOfRangeException(nameof(durationSec), durationSec, "must be positive");

        return (int)Math.Floor((1 - ContainerReserve) * maxBytes * 8 / durationSec);
    }
}
