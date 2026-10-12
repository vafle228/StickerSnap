using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Telegram;

namespace StickerSnap.Core.Render;

/// <summary>
/// Turns a segment of a scene into the sticker it makes (<see cref="StickerSpec"/>): the frames
/// from In to Out less the excluded ones, cut so the loop plays within Telegram's 3 s after the
/// speed and the ping-pong; the crop, or the whole picture for a stretch; the preset's side on the
/// longer side and even sides; the rate the frames come at. The spec is the best the sticker can
/// be: the fit ladder takes it from there, and the media engine renders it.
/// Several segments and the emoji preset (R17, R18) are only other inputs. A long sticker (R19)
/// is cut at 3 s too, until it gets limits of its own.
/// </summary>
public static class SceneCompiler
{
    /// <summary>The sticker of the segment <paramref name="segmentId"/> of <paramref name="scene"/>.</summary>
    /// <param name="source">What the media engine read from the project's source</param>
    /// <exception cref="AppException">
    /// bad_request when the scene has no such segment, or when the segment breaks a rule of
    /// <see cref="SceneValidator"/> against this source (after the source was replaced, a segment
    /// may run past its end)
    /// </exception>
    public static StickerSpec Compile(Scene scene, string segmentId, MediaInfo source)
    {
        var segment = scene.Segments.FirstOrDefault(segment => segment.Id == segmentId)
            ?? throw new AppException(ErrorCode.BadRequest, $"The scene has no segment {segmentId}");

        // The other segments are not rendered, so only this one has to keep the rules
        SceneValidator.EnsureValid(scene with { Segments = [segment] }, source);
        return Compile(segment, source, LimitsOf(segment.Preset));
    }

    /// <summary>The sticker of a segment that keeps the rules of <see cref="SceneValidator"/>.</summary>
    public static StickerSpec Compile(Segment segment, MediaInfo source, PresetLimits limits)
    {
        var fps = StickerSpec.StartRate(source, limits.MaxFps, segment.Speed);
        var clip = Clip(segment, source, limits, fps);

        return segment.Frame switch
        {
            SegmentFrame.Crop crop => Spec(clip, ToPixels(crop.Rect), crop.Rect.Width, crop.Rect.Height),
            SegmentFrame.Stretch stretch => Spec(
                clip, new PixelRect(0, 0, source.Width, source.Height), stretch.Aspect.Width, stretch.Aspect.Height),
            _ => throw new ArgumentOutOfRangeException(nameof(segment), segment.Frame, null),
        };

        StickerSpec Spec(StickerClip clip, PixelRect crop, double width, double height)
        {
            var size = segment.Preset == SegmentPreset.Emoji ? new PixelSize(limits.Side, limits.Side) : SizeOf(width, height, limits.Side);
            return new StickerSpec(clip, crop, size.Width, size.Height, fps, source.HasAlpha);
        }
    }

    /// <summary>A 512 px sticker or a 100×100 emoji (R17).</summary>
    public static PresetLimits LimitsOf(SegmentPreset preset) => preset switch
    {
        SegmentPreset.Sticker => TelegramLimits.Current.Sticker,
        SegmentPreset.Emoji => TelegramLimits.Current.Emoji,
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
    };

    /// <summary>
    /// The size of a sticker of this proportion: the longer side is the preset's, the shorter one
    /// keeps the proportion, rounded to an even number of pixels as yuv420 needs.
    /// </summary>
    public static PixelSize SizeOf(double width, double height, int side)
    {
        if (!(width > 0 && height > 0)) throw new ArgumentOutOfRangeException(nameof(width), $"{width}x{height} has no proportion");

        var shorter = Math.Clamp((int)Math.Round(side * Math.Min(width, height) / Math.Max(width, height) / 2, MidpointRounding.AwayFromZero) * 2, 2, side);
        return width >= height ? new PixelSize(side, shorter) : new PixelSize(shorter, side);
    }

    /// <summary>
    /// The frames a source plays in a second at speed 1: its average rate, or its frames over its
    /// length when the probe found no rate.
    /// </summary>
    public static double SourceRate(MediaInfo source)
    {
        if (source.Fps.IsValid) return source.Fps.ToDouble();
        if (source.FrameCount > 0 && source.DurationSec > 0) return source.FrameCount / source.DurationSec;
        throw new ArgumentException("The source has neither a frame rate nor a length", nameof(source));
    }

    /// <summary>
    /// The frames from In to Out less the excluded ones, and only as many as play within the
    /// limit at the sticker's rate: the frames past it would be cut from the file anyway.
    /// </summary>
    private static StickerClip Clip(Segment segment, MediaInfo source, PresetLimits limits, int fps)
    {
        var rate = SourceRate(source);
        var pingPong = segment.Loop == LoopMode.Pingpong;
        var excluded = segment.ExcludedFrames.Where(frame => frame >= segment.InFrame && frame <= segment.OutFrame).Distinct().Order().ToList();
        var frames = new SourceFrames(segment.InFrame, segment.OutFrame, excluded);

        // The source frames that fill the most forward frames the limit allows, rounded up: the
        // last one may run a little past the limit, and the file is cut at it
        var most = PlayLength.MaxForwardFrames(limits.MaxDurationSec, fps, pingPong);
        var needed = (int)Math.Ceiling(most * segment.Speed * rate / fps - 1e-9);
        frames = frames.Take(Math.Max(1, needed));

        return new StickerClip(frames, segment.Speed, pingPong, frames.Count / rate / segment.Speed, limits.MaxDurationSec);
    }

    /// <summary>A crop the validator found in whole pixels.</summary>
    private static PixelRect ToPixels(FrameRect rect) => new((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);
}
