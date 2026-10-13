using StickerSnap.Application.Media;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Render;
using StickerSnap.Core.Validation;

namespace StickerSnap.Application.Render;

/// <summary>
/// The render engine: a segment of a scene becomes its sticker file. <see cref="SceneCompiler"/>
/// works out the sticker; a final render goes down the fit ladder (<see cref="StickerFitService"/>)
/// and renders in two passes, then the file is read back and checked against Telegram's rules; a
/// draft (the exact preview) takes the same step from quick one-pass drafts and is not checked,
/// since only the final file is sent. A segment with fitting switched off (R7) is rendered once at
/// the first step, as it is, over the limit or not.
/// </summary>
public sealed class SegmentRenderService(IStickerFitService fitter, IStickerRenderer renderer, IMediaProbe probe) : ISegmentRenderService
{
    /// <summary>The share of a final render's progress the check takes.</summary>
    internal const double ValidateShare = 0.05;

    /// <inheritdoc />
    public async Task<RenderedSegment> RenderAsync(SegmentRender request, IProgress<RenderProgress>? progress, CancellationToken cancellationToken)
    {
        var segment = request.Scene.Segments.FirstOrDefault(segment => segment.Id == request.SegmentId)
            ?? throw new AppException(ErrorCode.BadRequest, $"The scene has no segment {request.SegmentId}");
        var spec = SceneCompiler.Compile(request.Scene, segment.Id, request.Source);
        var limits = SceneCompiler.LimitsOf(segment.Preset);
        var fit = new StickerFit(request.SourcePath, request.Source, spec, limits, LadderProfile.Sticker, request.OutputPath);

        var whole = ProgressPart.Whole(progress is null ? null : new RisingProgress(progress));
        whole.Report(0, RenderStage.Probe, null);
        var final = request.Quality == RenderEffort.Final;
        var rendering = final ? whole.Part(0, 1 - ValidateShare) : whole;
        var sticker = segment.AutoFit
            ? await FitAsync(fit, request.Quality, rendering, cancellationToken)
            : await AsIsAsync(fit, request.Quality, rendering, cancellationToken);

        StickerValidation? validation = null;
        if (final)
        {
            whole.Report(1 - ValidateShare, RenderStage.Validate, null);
            var media = await probe.ProbeAsync(sticker.Path, cancellationToken);
            validation = StickerValidator.Validate(media, segment.Preset, limits);
            whole.Report(1, RenderStage.Validate, null);
        }

        var made = spec.At(sticker.Rung);
        return new RenderedSegment(
            segment.Id,
            request.Quality,
            sticker.Path,
            sticker.SizeBytes,
            new StickerOutput(made.Width, made.Height, made.Fps, made.DurationSec),
            sticker.Rung.Crf,
            sticker.Rung.Concessions,
            sticker.Ladder,
            validation);
    }

    private async Task<Sticker> FitAsync(StickerFit fit, RenderEffort quality, ProgressPart progress, CancellationToken cancellationToken)
    {
        var reporter = progress.Target is null ? null : new PartProgress(progress);
        var fitted = quality == RenderEffort.Final
            ? await fitter.FitAsync(fit, reporter, cancellationToken)
            : await fitter.DraftAsync(fit, reporter, cancellationToken);

        // The steps passed by weigh what their estimates said; the step taken weighs its file, or
        // for a draft the final render's estimate, since the draft itself is larger
        var ladder = fitted.Steps.Select(step => new LadderTry(step.Rung, step.EstimatedBytes)).ToList();
        if (quality == RenderEffort.Final) ladder[^1] = ladder[^1] with { Bytes = fitted.SizeBytes };
        return new Sticker(fitted.Path, fitted.SizeBytes, fitted.Rung, ladder);
    }

    /// <summary>The first step of the ladder at its CRF: the size is whatever the picture needs.</summary>
    private async Task<Sticker> AsIsAsync(StickerFit fit, RenderEffort quality, ProgressPart progress, CancellationToken cancellationToken)
    {
        var rung = FitLadder.Rungs(fit.Spec, fit.Profile)[0];
        var spec = fit.Spec.At(rung);
        var attempt = RenderAttempt.Of(0, rung);
        progress.Report(0, RenderStage.Pass2, attempt);
        Sticker sticker;
        if (quality == RenderEffort.Final)
        {
            var rendered = await renderer.RenderAsync(
                new StickerRender(fit.SourcePath, fit.Source, spec, new RateControl.ConstantQuality(rung.Crf), RenderEffort.Final, fit.OutputPath),
                progress.Passes(attempt),
                cancellationToken);
            sticker = new Sticker(rendered.Path, rendered.SizeBytes, rung, [new LadderTry(rung, rendered.SizeBytes)]);
        }
        else
        {
            var draft = await renderer.DraftAsync(new StickerProbe(fit.SourcePath, fit.Source, spec, rung.Crf), fit.OutputPath, cancellationToken);
            sticker = new Sticker(draft.Path, draft.SizeBytes, rung, [new LadderTry(rung, draft.EstimatedBytes)]);
        }

        progress.Report(1, RenderStage.Pass2, attempt);
        return sticker;
    }

    private sealed record Sticker(string Path, long SizeBytes, FitRung Rung, IReadOnlyList<LadderTry> Ladder);

    /// <summary>The fit's own 0..1, put into the part of the render it takes.</summary>
    private sealed class PartProgress(ProgressPart part) : IProgress<RenderProgress>
    {
        public void Report(RenderProgress value) => part.Report(value.Fraction, value.Stage, value.Attempt);
    }

    /// <summary>
    /// Holds the share at the highest one reported: the engine's own count may step back a frame,
    /// and a progress bar never should.
    /// </summary>
    private sealed class RisingProgress(IProgress<RenderProgress> target) : IProgress<RenderProgress>
    {
        private readonly Lock _lock = new();
        private double _highest;

        public void Report(RenderProgress value)
        {
            lock (_lock)
            {
                _highest = Math.Max(_highest, value.Fraction);
                target.Report(value with { Fraction = _highest });
            }
        }
    }
}

/// <summary>What to render: a segment of a scene cut from a source, as a draft or the final file.</summary>
/// <param name="SourcePath">The project's source on this machine</param>
/// <param name="Source">What the media engine read from it</param>
/// <param name="Scene">The scene the segment belongs to; for a preview, the one being edited</param>
/// <param name="Quality">A draft for the exact preview, or the final file for an export</param>
/// <param name="OutputPath">A .webm file, replaced</param>
public sealed record SegmentRender(
    string SourcePath,
    MediaInfo Source,
    Scene Scene,
    string SegmentId,
    RenderEffort Quality,
    string OutputPath);

/// <summary>A segment rendered: what the contract's ExportResult and the preview tell about it.</summary>
/// <param name="Bytes">
/// Of the file on disk; a draft is larger than the final render it stands for, whose estimate is
/// the last step of <paramref name="Ladder"/>
/// </param>
/// <param name="Output">What the sticker came out as</param>
/// <param name="Crf">The quality the sticker is held to: the CRF of the step taken</param>
/// <param name="Concessions">«Чем пожертвовали»: what the step taken gives up; empty when nothing was</param>
/// <param name="Ladder">«Как подбирали»: the steps tried, the one taken last</param>
/// <param name="Validation">Telegram's checklist of the file; null for a draft, which is never sent</param>
public sealed record RenderedSegment(
    string SegmentId,
    RenderEffort Quality,
    string Path,
    long Bytes,
    StickerOutput Output,
    int Crf,
    IReadOnlyList<Concession> Concessions,
    IReadOnlyList<LadderTry> Ladder,
    StickerValidation? Validation);
