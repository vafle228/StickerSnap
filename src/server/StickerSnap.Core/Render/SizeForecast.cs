using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Telegram;

namespace StickerSnap.Core.Render;

/// <summary>
/// The size forecast of the inspector (3a «Экспорт», 3c G) beyond what the media engine measures:
/// the size a sticker must stay within, and the edits that bring it there. A sticker at a constant
/// quality weighs about the same for each frame it plays, so the edits are found by that proportion
/// from the one size measured: <see cref="Suggestions"/> keep the bytes per played frame and look for
/// the fewest frames given up.
/// </summary>
public static class SizeForecast
{
    /// <summary>«Ускорить» goes in steps of this, as the editor's speed control does.</summary>
    public const double SpeedStep = 0.05;

    /// <summary>
    /// The most a sticker may weigh and still be fine: with fitting on, the budget at which the
    /// export takes the first step of the ladder and gives nothing up; with fitting off, the limit
    /// itself, since the file is rendered as it is and checked against it.
    /// </summary>
    public static long Allowance(PresetLimits limits, bool autoFit) => autoFit ? FitLadder.Budget(limits) : limits.MaxBytes;

    /// <summary>
    /// The frames the sticker of <paramref name="segment"/> plays at the first step of the ladder:
    /// what <see cref="Suggestions"/> weighs.
    /// </summary>
    public static int PlayedFrames(Segment segment, MediaInfo source, PresetLimits limits, LadderProfile profile)
    {
        var spec = SceneCompiler.Compile(segment, source, limits);
        return spec.At(FitLadder.Rungs(spec, profile)[0]).PlayedFrames;
    }

    /// <summary>
    /// «Укоротить» to the latest Out frame and «Ускорить» to the slowest speed whose sticker, at the
    /// bytes per frame of <paramref name="bytes"/>, is within <paramref name="allowance"/>; either is
    /// left out when no such edit exists (a segment cut at 3 s plays as many frames however fast it
    /// goes; a low frame rate source plays the same frames faster until the sticker's rate is full).
    /// </summary>
    /// <param name="segment">A segment that keeps the rules of <see cref="SceneValidator"/></param>
    /// <param name="bytes">What the sticker of <paramref name="segment"/> weighs at the first step of the ladder</param>
    public static IReadOnlyList<SizeSuggestion> Suggestions(
        Segment segment,
        MediaInfo source,
        PresetLimits limits,
        LadderProfile profile,
        long bytes,
        long allowance)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        var perFrame = (double)bytes / PlayedFrames(segment, source, limits, profile);
        bool Fits(Segment edited) => perFrame * PlayedFrames(edited, source, limits, profile) <= allowance;

        List<SizeSuggestion> suggestions = [];
        if (Shortest(segment) is { } trimmed)
        {
            // The frames played only grow with the Out frame, so the latest that fits is found by halves
            int low = trimmed.OutFrame, high = segment.OutFrame - 1;
            if (Fits(trimmed))
            {
                while (low < high)
                {
                    var middle = (low + high + 1) / 2;
                    if (Fits(EndAt(segment, middle))) low = middle;
                    else high = middle - 1;
                }

                var spec = SceneCompiler.Compile(EndAt(segment, low), source, limits);
                suggestions.Add(new SizeSuggestion.Trim(low, spec.At(FitLadder.Rungs(spec, profile)[0]).DurationSec));
            }
        }

        // The steps above the current speed, each checked: there are at most 75 of them
        for (var step = Math.Floor(segment.Speed / SpeedStep + 1e-9) + 1; step * SpeedStep <= Segment.MaxSpeed + 1e-9; step++)
        {
            var speed = Math.Round(step * SpeedStep, 2);
            if (!Fits(segment with { Speed = speed })) continue;
            suggestions.Add(new SizeSuggestion.Speed(speed));
            break;
        }

        return suggestions;
    }

    /// <summary>The segment ended at its first frame that plays, or null when that is already its end.</summary>
    private static Segment? Shortest(Segment segment)
    {
        var first = segment.InFrame;
        while (segment.ExcludedFrames.Contains(first)) first++;
        return first < segment.OutFrame ? EndAt(segment, first) : null;
    }

    /// <summary>The segment ended at <paramref name="outFrame"/>, without the excluded frames past it.</summary>
    private static Segment EndAt(Segment segment, int outFrame) => segment with
    {
        OutFrame = outFrame,
        ExcludedFrames = [.. segment.ExcludedFrames.Where(frame => frame <= outFrame)],
    };
}
