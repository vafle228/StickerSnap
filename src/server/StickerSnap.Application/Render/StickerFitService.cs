using StickerSnap.Application.Media;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;
using StickerSnap.Core.Render;
using StickerSnap.Core.Telegram;

namespace StickerSnap.Application.Render;

/// <summary>
/// Renders a sticker under its preset's size limit (R7): estimates the steps of the
/// <see cref="FitLadder"/> at their CRF until one fits, renders that one in two passes at the
/// target bitrate and corrects the bitrate while the file misses (see <see cref="FitLadder.Retarget"/>).
/// </summary>
public sealed class StickerFitService(IStickerRenderer renderer)
{
    /// <summary>Two-pass renders of the chosen step before the last good one is rendered again.</summary>
    public const int MaxFinalAttempts = 3;

    /// <exception cref="AppException">
    /// `cannot_fit` when no two-pass render came under the limit; the engine's errors as
    /// <see cref="IStickerRenderer.RenderAsync"/> has them
    /// </exception>
    public async Task<FittedSticker> FitAsync(StickerFit fit, CancellationToken cancellationToken)
    {
        var rungs = FitLadder.Rungs(fit.Spec, fit.Profile);
        List<FitStep> steps = [];
        foreach (var rung in rungs)
        {
            var estimate = await renderer.EstimateAsync(
                new StickerProbe(fit.SourcePath, fit.Source, fit.Spec.At(rung), rung.Crf), cancellationToken);
            steps.Add(new FitStep(rung, estimate, FitLadder.Fits(estimate, fit.Limits)));
            if (steps[^1].Fits) break;
        }

        // When no step fits at its CRF, the last one still meets the size in two passes, below its CRF
        var chosen = steps[^1].Rung;
        var spec = fit.Spec.At(chosen);
        var rate = new RateControl.TwoPass(TargetBitrate.BitsPerSecond(fit.Limits.MaxBytes, spec.DurationSec, spec.KeepAlpha));
        List<FinalAttempt> attempts = [];
        FinalAttempt? fitting = null;
        while (true)
        {
            var attempt = await RenderAsync(fit, spec, rate, cancellationToken);
            attempts.Add(attempt);
            if (attempt.Bytes <= fit.Limits.MaxBytes) fitting = attempt;

            var next = attempts.Count < MaxFinalAttempts ? FitLadder.Retarget(rate, attempt.Bytes, fit.Limits, spec.KeepAlpha) : null;
            if (next is null) break;
            rate = next;
        }

        if (attempts[^1].Bytes > fit.Limits.MaxBytes)
        {
            // A fill that overshot, and its correction too: the file on disk is too big, the earlier one was not
            if (fitting is null)
                throw new AppException(
                    ErrorCode.CannotFit,
                    $"{Path.GetFileName(fit.SourcePath)} stays over {fit.Limits.MaxBytes} B after {attempts.Count} two-pass renders at {chosen}");
            attempts.Add(await RenderAsync(fit, spec, fitting.Rate, cancellationToken));
        }

        return new FittedSticker(fit.OutputPath, attempts[^1].Bytes, chosen, steps[^1].Fits, steps, attempts);
    }

    private async Task<FinalAttempt> RenderAsync(StickerFit fit, StickerSpec spec, RateControl.TwoPass rate, CancellationToken cancellationToken)
    {
        var rendered = await renderer.RenderAsync(
            new StickerRender(fit.SourcePath, fit.Source, spec, rate, RenderEffort.Final, fit.OutputPath), cancellationToken);
        return new FinalAttempt(rate, rendered.SizeBytes);
    }
}

/// <param name="Spec">The sticker as edited; the ladder lowers its rate and detail, never its size</param>
/// <param name="OutputPath">A .webm file, replaced</param>
public sealed record StickerFit(
    string SourcePath,
    MediaInfo Source,
    StickerSpec Spec,
    PresetLimits Limits,
    LadderProfile Profile,
    string OutputPath);

/// <summary>A step of the ladder tried: «Как подбирали».</summary>
/// <param name="EstimatedBytes">Of the final render at the step's CRF</param>
public sealed record FitStep(FitRung Rung, long EstimatedBytes, bool Fits);

/// <summary>One two-pass render of the chosen step.</summary>
public sealed record FinalAttempt(RateControl.TwoPass Rate, long Bytes);

/// <param name="Rung">The step rendered: its CRF and its concessions are what the user sees</param>
/// <param name="HoldsCrf">
/// The step fitted at its CRF, so the file is at least that good; false when no step did and the
/// last one was squeezed in by the two passes
/// </param>
/// <param name="Steps">The steps estimated, the chosen one last</param>
/// <param name="Attempts">The two-pass renders, the one on disk last</param>
public sealed record FittedSticker(
    string Path,
    long SizeBytes,
    FitRung Rung,
    bool HoldsCrf,
    IReadOnlyList<FitStep> Steps,
    IReadOnlyList<FinalAttempt> Attempts)
{
    public IReadOnlyList<Concession> Concessions => Rung.Concessions;
}
