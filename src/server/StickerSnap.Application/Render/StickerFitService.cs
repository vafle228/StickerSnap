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

    /// <summary>The share of the progress the estimates take; the two-pass renders take the rest.</summary>
    internal const double ProbeShare = 0.1;

    /// <param name="progress">Gets the estimates as `probe`, then the passes of each render</param>
    /// <exception cref="AppException">
    /// `cannot_fit` when no two-pass render came under the limit; the engine's errors as
    /// <see cref="IStickerRenderer.RenderAsync"/> has them
    /// </exception>
    public async Task<FittedSticker> FitAsync(StickerFit fit, IProgress<RenderProgress>? progress, CancellationToken cancellationToken)
    {
        var whole = ProgressPart.Whole(progress);
        var rungs = FitLadder.Rungs(fit.Spec, fit.Profile);
        var probing = whole.Part(0, ProbeShare);
        List<FitStep> steps = [];
        foreach (var rung in rungs)
        {
            probing.Piece(steps.Count).Report(0, RenderStage.Probe, RenderAttempt.Of(steps.Count, rung));
            var estimate = await renderer.EstimateAsync(
                new StickerProbe(fit.SourcePath, fit.Source, fit.Spec.At(rung), rung.Crf), cancellationToken);
            steps.Add(new FitStep(rung, estimate, FitLadder.Fits(estimate, fit.Limits)));
            if (steps[^1].Fits) break;
        }

        // When no step fits at its CRF, the last one still meets the size in two passes, below its CRF
        var chosen = steps[^1].Rung;
        var attempt = RenderAttempt.Of(steps.Count - 1, chosen);
        var encoding = whole.Part(ProbeShare, 1);
        var spec = fit.Spec.At(chosen);
        var rate = new RateControl.TwoPass(TargetBitrate.BitsPerSecond(fit.Limits.MaxBytes, spec.DurationSec, spec.KeepAlpha));
        List<FinalAttempt> attempts = [];
        FinalAttempt? fitting = null;
        while (true)
        {
            var rendered = await RenderAsync(fit, spec, rate, encoding.Piece(attempts.Count).Passes(attempt), cancellationToken);
            attempts.Add(rendered);
            if (rendered.Bytes <= fit.Limits.MaxBytes) fitting = rendered;

            var next = attempts.Count < MaxFinalAttempts ? FitLadder.Retarget(rate, rendered.Bytes, fit.Limits, spec.KeepAlpha) : null;
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
            attempts.Add(await RenderAsync(fit, spec, fitting.Rate, encoding.Piece(attempts.Count).Passes(attempt), cancellationToken));
        }

        whole.Report(1, RenderStage.Pass2, attempt);
        return new FittedSticker(fit.OutputPath, attempts[^1].Bytes, chosen, steps[^1].Fits, steps, attempts);
    }

    /// <summary>
    /// The preview of <see cref="FitAsync"/>: the same steps estimated from drafts, each kept at
    /// the output path, so the draft of the step the final render would take is left there. No
    /// step fitting leaves the last one, which the final render squeezes in with its two passes.
    /// </summary>
    /// <param name="progress">Gets each draft as `pass2`: a draft is the one pass the user waits for</param>
    /// <exception cref="AppException">The engine's errors as <see cref="IStickerRenderer.RenderAsync"/> has them</exception>
    public async Task<FittedSticker> DraftAsync(StickerFit fit, IProgress<RenderProgress>? progress, CancellationToken cancellationToken)
    {
        var whole = ProgressPart.Whole(progress);
        var rungs = FitLadder.Rungs(fit.Spec, fit.Profile);
        List<FitStep> steps = [];
        DraftSticker? draft = null;
        foreach (var rung in rungs)
        {
            whole.Piece(steps.Count).Report(0, RenderStage.Pass2, RenderAttempt.Of(steps.Count, rung));
            draft = await renderer.DraftAsync(
                new StickerProbe(fit.SourcePath, fit.Source, fit.Spec.At(rung), rung.Crf), fit.OutputPath, cancellationToken);
            steps.Add(new FitStep(rung, draft.EstimatedBytes, FitLadder.Fits(draft.EstimatedBytes, fit.Limits)));
            if (steps[^1].Fits) break;
        }

        whole.Report(1, RenderStage.Pass2, RenderAttempt.Of(steps.Count - 1, steps[^1].Rung));
        return new FittedSticker(draft!.Path, draft.SizeBytes, steps[^1].Rung, steps[^1].Fits, steps, []);
    }

    private async Task<FinalAttempt> RenderAsync(
        StickerFit fit,
        StickerSpec spec,
        RateControl.TwoPass rate,
        IProgress<PassProgress>? progress,
        CancellationToken cancellationToken)
    {
        var rendered = await renderer.RenderAsync(
            new StickerRender(fit.SourcePath, fit.Source, spec, rate, RenderEffort.Final, fit.OutputPath), progress, cancellationToken);
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
/// <param name="Attempts">The two-pass renders, the one on disk last; none for a draft, which is the last step's estimate</param>
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
