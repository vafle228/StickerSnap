using StickerSnap.Application.Media;
using StickerSnap.Application.Projects;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Render;

namespace StickerSnap.Application.Render;

/// <summary>
/// What rendering the stickers of a project would run, without running it: each segment compiled
/// by <see cref="SceneCompiler"/> at the first step of the fit ladder, in two passes at the target
/// bitrate, as the media engine's commands. For a person checking the compiler by hand.
/// </summary>
public sealed class RenderPlanService(IProjectStore projects, IStickerRenderer renderer)
{
    /// <exception cref="AppException">As <see cref="IProjectStore.ReadAsync"/></exception>
    public async Task<IReadOnlyList<SegmentPlan>> DescribeAsync(string projectPath, CancellationToken cancellationToken)
    {
        var project = await projects.ReadAsync(projectPath, cancellationToken);
        var media = project.Source.Media;
        List<SegmentPlan> plans = [];
        foreach (var segment in project.Scene.Segments)
        {
            StickerSpec spec;
            try
            {
                spec = SceneCompiler.Compile(project.Scene, segment.Id, media);
            }
            catch (AppException e)
            {
                plans.Add(new SegmentPlan(segment, null, null, e.Message));
                continue;
            }

            var limits = SceneCompiler.LimitsOf(segment.Preset);
            spec = spec.At(FitLadder.Rungs(spec, LadderProfile.Sticker)[0]);
            var rate = new RateControl.TwoPass(TargetBitrate.BitsPerSecond(limits.MaxBytes, spec.DurationSec, spec.KeepAlpha));
            var output = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(projectPath))!, $"{Path.GetFileNameWithoutExtension(projectPath)}.{segment.Id}.webm");
            var render = new StickerRender(project.Source.Path, media, spec, rate, RenderEffort.Final, output);
            plans.Add(new SegmentPlan(segment, spec, renderer.Describe(render), null));
        }

        return plans;
    }
}

/// <param name="Spec">Null when the segment breaks a rule of the scene against the source</param>
/// <param name="Commands">The media engine's commands, one per line; null with <paramref name="Problem"/></param>
/// <param name="Problem">Why the segment makes no sticker</param>
public sealed record SegmentPlan(Segment Segment, StickerSpec? Spec, string? Commands, string? Problem);
