namespace StickerSnap.Core.Render;

/// <summary>
/// How long a sticker plays, counted on the frames it ends up with: after the speed and the frame
/// rate, and after ping-pong (R5), which plays the frames forward and then back. Telegram's 3 s, the
/// bitrate of <see cref="TargetBitrate"/> and the editor's limit on the segment (R2, R4) all take
/// this length (fit spike, open question 12).
/// </summary>
public static class PlayLength
{
    /// <summary>
    /// The frames played in one loop: a ping-pong goes back without either end frame, which the
    /// next forward run and the turn already show, so n frames make 2n − 2 and the loop has no stall.
    /// </summary>
    public static int Frames(int forwardFrames, bool pingPong)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(forwardFrames);
        return pingPong && forwardFrames > 1 ? 2 * forwardFrames - 2 : forwardFrames;
    }

    public static double Seconds(int forwardFrames, int fps, bool pingPong)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fps);
        return (double)Frames(forwardFrames, pingPong) / fps;
    }

    /// <summary>
    /// The most frames a segment may run forward and still play within <paramref name="maxSec"/>:
    /// 90 at 30 fps for 3 s, 46 with ping-pong (a little over 1.5 s of the source at speed 1).
    /// </summary>
    public static int MaxForwardFrames(double maxSec, int fps, bool pingPong)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fps);
        if (!(maxSec > 0)) throw new ArgumentOutOfRangeException(nameof(maxSec), maxSec, "must be positive");

        // A hair of slack, so 3 s at 30 fps is 90 frames whatever the rounding of 3 * 30
        var frames = (int)Math.Floor(maxSec * fps + 1e-9);
        return pingPong ? Math.Max(1, (frames + 2) / 2) : Math.Max(1, frames);
    }
}
