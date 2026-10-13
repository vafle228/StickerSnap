using StickerSnap.Application.Exports;
using StickerSnap.Application.Media;
using StickerSnap.Application.Projects;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Render;
using StickerSnap.Core.Telegram;

namespace StickerSnap.Application.Estimates;

/// <summary>
/// <see cref="IEstimateService"/> over the media engine's estimate, the one the export climbs its
/// ladder by (fit spike, 12 October): a quick draft of a step at its CRF stands for the final file.
/// The first step is always measured; when it is over and fitting is on, the next ones are measured
/// until one fits, as the export would. Estimates run one at a time, so a stream of edits does not
/// start a stream of encoders, and a request the editor has given up on leaves the queue.
/// </summary>
public sealed class EstimateService(IProjectService projects, IStickerRenderer renderer) : IEstimateService, IDisposable
{
    /// <summary>Forecasts kept in memory: the segments of a few editing sessions.</summary>
    internal const int Kept = 256;

    /// <summary>Drafts of one suggested edit before it is given up: each about a fifth of a second.</summary>
    internal const int MeasuresPerSuggestion = 3;

    private static readonly LadderProfile Profile = LadderProfile.Sticker;

    private readonly EstimateCache _cache = new(Kept);
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <inheritdoc />
    public async Task<SegmentEstimate> EstimateAsync(Guid projectId, Segment segment, CancellationToken cancellationToken)
    {
        var source = (await RenderSources.ProjectWithSourceAsync(projects, projectId, cancellationToken)).Project.Source;
        EnsureValid(segment, source.Media);

        var key = SegmentSignature.Of(segment, source);
        if (_cache.Find(key) is { } known) return known;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Another request may have measured the same segment while this one waited
            if (_cache.Find(key) is { } measured) return measured;
            var estimate = await MeasureAsync(segment, source, cancellationToken);
            _cache.Add(key, estimate);
            return estimate;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private async Task<SegmentEstimate> MeasureAsync(Segment segment, ProjectSource source, CancellationToken cancellationToken)
    {
        var limits = SceneCompiler.LimitsOf(segment.Preset);
        var spec = SceneCompiler.Compile(segment, source.Media, limits);
        var rungs = FitLadder.Rungs(spec, Profile);
        var allowance = SizeForecast.Allowance(limits, segment.AutoFit);

        var first = spec.At(rungs[0]);
        var bytes = await MeasureAsync(source, spec, rungs[0], cancellationToken);
        var fits = bytes <= allowance;

        IReadOnlyList<Concession> likely = [];
        if (!fits && segment.AutoFit)
        {
            // The step the export will take: the first that fits, or the last, squeezed in by two passes
            foreach (var rung in rungs.Skip(1))
            {
                likely = rung.Concessions;
                if (FitLadder.Fits(await MeasureAsync(source, spec, rung, cancellationToken), limits)) break;
            }
        }

        return new SegmentEstimate(
            bytes,
            limits.MaxBytes,
            fits,
            new StickerOutput(first.Width, first.Height, first.Fps, first.DurationSec),
            likely,
            fits ? [] : await SuggestAsync(segment, source, limits, bytes, allowance, cancellationToken));
    }

    /// <summary>
    /// The edits of <see cref="SizeForecast.Suggestions"/>, each measured as it would be after the
    /// click: the bytes of a clip are not spread evenly over its frames, so an edit found by the
    /// proportion may still be over. One over is worked out again from its own measure, which
    /// gives up a little more, at most <see cref="MeasuresPerSuggestion"/> times; an edit that
    /// never fits is not offered.
    /// </summary>
    private async Task<IReadOnlyList<SizeSuggestion>> SuggestAsync(
        Segment segment,
        ProjectSource source,
        PresetLimits limits,
        long bytes,
        long allowance,
        CancellationToken cancellationToken)
    {
        List<SizeSuggestion> settled = [];
        foreach (var first in SizeForecast.Suggestions(segment, source.Media, limits, Profile, bytes, allowance))
        {
            SizeSuggestion? suggestion = first;
            for (var measures = 0; suggestion is not null && measures < MeasuresPerSuggestion; measures++)
            {
                var edited = suggestion.ApplyTo(segment);
                var spec = SceneCompiler.Compile(edited, source.Media, limits);
                var weight = await MeasureAsync(source, spec, FitLadder.Rungs(spec, Profile)[0], cancellationToken);
                if (weight <= allowance)
                {
                    settled.Add(suggestion);
                    break;
                }

                var kind = suggestion.GetType();
                suggestion = SizeForecast.Suggestions(edited, source.Media, limits, Profile, weight, allowance)
                    .FirstOrDefault(next => next.GetType() == kind);
            }
        }

        return settled;
    }

    private Task<long> MeasureAsync(ProjectSource source, StickerSpec spec, FitRung rung, CancellationToken cancellationToken) =>
        renderer.EstimateAsync(new StickerProbe(source.Path, source.Media, spec.At(rung), rung.Crf), cancellationToken);

    /// <summary>The rules of the scene, with the fields named as the body of POST …/estimate has them.</summary>
    /// <exception cref="AppException">bad_request with the broken fields under `segment.`</exception>
    private static void EnsureValid(Segment segment, MediaInfo source)
    {
        const string prefix = "segments[0]";
        var scene = new Scene(Scene.CurrentSchemaVersion, [new Layer.Video("video")], [segment]);
        var problems = SceneValidator.Validate(scene, source)
            .Where(problem => problem.Field.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();
        if (problems.Count == 0) return;

        throw new AppException(
            ErrorCode.BadRequest,
            $"The segment breaks {problems.Count} rule(s): " +
            string.Join(", ", problems.Select(problem => $"{problem.Field} {problem.Rule}")))
        {
            Fields = problems
                .GroupBy(problem => "segment" + problem.Field[prefix.Length..])
                .ToDictionary(group => group.Key, group => group.Select(problem => problem.Rule).ToArray()),
        };
    }
}
