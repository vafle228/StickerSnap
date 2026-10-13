using StickerSnap.Core.Errors;

namespace StickerSnap.Core.Media;

/// <summary>
/// The frames of the timeline's «Кадры» as one picture (contract, getProjectFrames): <see cref="Count"/>
/// frames of the source from <see cref="From"/>, in one row left to right, each <see cref="Height"/>
/// tall and as wide as the source's proportion gives. Frame n is the n-th frame the source decodes to,
/// the same numbers the scene and the render count (G8).
/// </summary>
public sealed record FrameStrip
{
    /// <summary>Frames in one strip: the SPA asks for 30 at a time</summary>
    public const int MaxCount = 120;

    /// <summary>Of one frame: the canvas poster asks for up to 720</summary>
    public const int MaxHeight = 720;

    /// <summary>Of the whole strip: a WEBP picture is at most 16383 px a side</summary>
    public const int MaxWidth = 16383;

    private FrameStrip(int from, int count, int height, int frameWidth)
    {
        From = from;
        Count = count;
        Height = height;
        FrameWidth = frameWidth;
    }

    public int From { get; }

    public int Count { get; }

    public int Height { get; }

    /// <summary>Of one frame, from the source's proportion as shown; at least a pixel</summary>
    public int FrameWidth { get; }

    public int Width => FrameWidth * Count;

    /// <summary>The last frame of the strip, included.</summary>
    public int Last => From + Count - 1;

    /// <summary>The strip the query asks of <paramref name="source"/>.</summary>
    /// <exception cref="AppException">
    /// `bad_request` with the field at fault: `out_of_range` for frames past the source's end, a
    /// count or a height past the limits; `too_wide` for a strip wider than a picture can be
    /// </exception>
    public static FrameStrip Of(MediaInfo source, int from, int count, int height)
    {
        if (from < 0 || from >= source.FrameCount) throw OutOfRange("from", $"Frame {from} is not in the source's {source.FrameCount}");
        if (count < 1 || count > MaxCount || (long)from + count > source.FrameCount)
            throw OutOfRange("count", $"{count} frames from {from} do not fit in 1..{MaxCount} and the source's {source.FrameCount}");
        if (height < 1 || height > MaxHeight) throw OutOfRange("height", $"A frame {height} px tall is not in 1..{MaxHeight}");

        var frameWidth = Math.Max(1, (int)Math.Round((double)height * source.Width / Math.Max(1, source.Height), MidpointRounding.AwayFromZero));
        if ((long)frameWidth * count > MaxWidth)
        {
            throw new AppException(ErrorCode.BadRequest, $"{count} frames {frameWidth} px wide make a strip wider than {MaxWidth} px")
            {
                Fields = new Dictionary<string, string[]> { ["count"] = ["too_wide"] },
            };
        }

        return new FrameStrip(from, count, height, frameWidth);
    }

    private static AppException OutOfRange(string field, string message) => new(ErrorCode.BadRequest, message)
    {
        Fields = new Dictionary<string, string[]> { [field] = ["out_of_range"] },
    };
}
