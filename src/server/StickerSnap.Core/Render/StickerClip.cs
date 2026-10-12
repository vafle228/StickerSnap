namespace StickerSnap.Core.Render;

/// <summary>
/// The part of the source a sticker plays and how it plays it: the frames, the speed, the loop,
/// and the longest the loop may last. How many frames that makes depends on the frame rate the
/// sticker is rendered at, which the fit ladder lowers, so it is worked out per rate.
/// </summary>
/// <param name="Speed">1 as recorded, 2 twice as fast</param>
/// <param name="PingPong">Forwards, then backwards (R5)</param>
/// <param name="ForwardSec">
/// One forward run of <paramref name="Frames"/> at <paramref name="Speed"/>: their number over the
/// source's frame rate and the speed
/// </param>
/// <param name="MaxSec">The longest a loop may play: Telegram's 3 s</param>
public sealed record StickerClip(SourceFrames Frames, double Speed, bool PingPong, double ForwardSec, double MaxSec)
{
    /// <summary>
    /// The frames one forward run makes at <paramref name="fps"/>: its length times the rate,
    /// rounded as a resampler rounds it, and never more than plays within <see cref="MaxSec"/>.
    /// </summary>
    public int ForwardFrames(int fps)
    {
        var frames = (int)Math.Round(ForwardSec * fps, MidpointRounding.AwayFromZero);
        return Math.Clamp(frames, 1, PlayLength.MaxForwardFrames(MaxSec, fps, PingPong));
    }

    /// <summary>The frames of a whole loop at <paramref name="fps"/>: a ping-pong plays most of them twice.</summary>
    public int PlayedFrames(int fps) => PlayLength.Frames(ForwardFrames(fps), PingPong);

    /// <summary>How long a whole loop plays at <paramref name="fps"/>.</summary>
    public double Seconds(int fps) => PlayLength.Seconds(ForwardFrames(fps), fps, PingPong);
}
