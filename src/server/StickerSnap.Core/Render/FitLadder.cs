using StickerSnap.Core.Telegram;

namespace StickerSnap.Core.Render;

/// <summary>
/// How a sticker is brought under the size limit (R7), decided by the fit spike (B5, 12 October).
/// <para>
/// The final render is two passes at <see cref="TargetBitrate"/>: it lands near the limit whatever
/// the picture, so its size says nothing about what was given up; on a busy clip the quality drops
/// instead. The signal to step down is therefore the size at a constant quality: each step of
/// <see cref="Rungs"/> is estimated at its CRF, and the first one that fits the <see cref="Budget"/>
/// is rendered in two passes. The file is then at least as good as that CRF, and the concessions of
/// that step are all that was given up.
/// </para>
/// </summary>
public static class FitLadder
{
    /// <summary>A transparent sticker under this share of the budget gets a second try with more bits.</summary>
    public const double UnderfillShare = 0.75;

    /// <summary>Taken off a retarget over the plain proportion: short clips overshoot again by a few percent.</summary>
    private const double RetargetMargin = 0.98;

    /// <summary>Taken off a fill: the alpha stream does not grow in step with the picture.</summary>
    private const double FillMargin = 0.9;

    /// <summary>
    /// A fill gives each stream of a transparent sticker at most the whole bitrate an opaque one
    /// gets: twice its half.
    /// </summary>
    private const double MaxFill = 2;

    /// <summary>The bytes a final render aims at: the limit less the container's reserve.</summary>
    public static long Budget(PresetLimits limits) => (long)Math.Floor((1 - TargetBitrate.ContainerReserve) * limits.MaxBytes);

    /// <summary>
    /// The steps from the best down: the frame rates of the profile below the sticker's own, then
    /// the higher CRFs at the lowest rate, then less detail at the highest CRF. The sticker's size
    /// never changes, so one side stays the preset's; the detail is even on both sides.
    /// </summary>
    public static IReadOnlyList<FitRung> Rungs(StickerSpec spec, LadderProfile profile)
    {
        ArgumentOutOfRangeException.ThrowIfZero(profile.FrameRates.Count);
        ArgumentOutOfRangeException.ThrowIfZero(profile.Crfs.Count);

        var top = Math.Min(spec.Fps, profile.FrameRates[0]);
        var rates = profile.FrameRates.Where(rate => rate < top).Prepend(top).ToList();
        var size = spec.Size;
        var first = (Fps: top, Crf: profile.Crfs[0], Detail: size);

        List<(int Fps, int Crf, PixelSize Detail)> steps = [.. rates.Select(rate => (rate, first.Crf, size))];
        steps.AddRange(profile.Crfs.Skip(1).Select(crf => (rates[^1], crf, size)));
        steps.AddRange(profile.DetailScales.Select(scale => (rates[^1], profile.Crfs[^1], Shrink(size, scale))));

        return steps.Select(step => new FitRung(step.Fps, step.Crf, step.Detail, Concessions(first, step))).ToList();
    }

    /// <summary>A step fits when its final render, estimated at its CRF, is within the budget.</summary>
    public static bool Fits(long estimatedBytes, PresetLimits limits) => estimatedBytes <= Budget(limits);

    /// <summary>
    /// The bitrate of one more two-pass attempt, or null when <paramref name="bytes"/> will do.
    /// Over the limit (a clip under a second overshoots: its 15–30 frames are too few for the
    /// encoder's rate control) the bitrate shrinks in proportion. A transparent sticker far under
    /// the budget (the alpha stream rarely needs its half) gets the unused bits for its picture.
    /// </summary>
    public static RateControl.TwoPass? Retarget(RateControl.TwoPass rate, long bytes, PresetLimits limits, bool transparent)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);
        var budget = Budget(limits);
        if (bytes > limits.MaxBytes)
            return new RateControl.TwoPass((int)Math.Floor(rate.BitsPerSecond * RetargetMargin * budget / bytes));

        if (!transparent || bytes >= UnderfillShare * budget) return null;
        return new RateControl.TwoPass((int)Math.Floor(rate.BitsPerSecond * Math.Min(MaxFill, FillMargin * budget / bytes)));
    }

    private static PixelSize Shrink(PixelSize size, double scale) => new(Even(size.Width * scale), Even(size.Height * scale));

    private static int Even(double side) => Math.Max(2, (int)Math.Round(side / 2) * 2);

    private static List<Concession> Concessions((int Fps, int Crf, PixelSize Detail) first, (int Fps, int Crf, PixelSize Detail) step)
    {
        List<Concession> concessions = [];
        if (step.Fps != first.Fps) concessions.Add(new Concession.Fps(first.Fps, step.Fps));
        if (step.Crf != first.Crf) concessions.Add(new Concession.Crf(first.Crf, step.Crf));
        if (step.Detail != first.Detail) concessions.Add(new Concession.Scale(first.Detail.LongerSide, step.Detail.LongerSide));
        return concessions;
    }
}
