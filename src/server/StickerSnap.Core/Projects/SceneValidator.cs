using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;

namespace StickerSnap.Core.Projects;

/// <summary>
/// The rules a scene keeps against its source before it is saved: one video layer, segments
/// inside the source's frames, a speed of 0.25–4, a crop in whole pixels inside the picture.
/// A .ssproj is read without them: after the source is replaced, a segment may run past its end
/// until the user moves it.
/// </summary>
public static class SceneValidator
{
    /// <summary>Every rule <paramref name="scene"/> breaks, in the order of its fields; empty for a valid one.</summary>
    /// <param name="source">What the media engine read from the project's source</param>
    public static IReadOnlyList<SceneProblem> Validate(Scene scene, MediaInfo source)
    {
        var problems = new List<SceneProblem>();

        if (scene.SchemaVersion != Scene.CurrentSchemaVersion)
            problems.Add(new SceneProblem("schemaVersion", SceneRules.Unsupported));

        if (scene.Layers.Count != 1 || scene.Layers[0] is not Layer.Video)
            problems.Add(new SceneProblem("layers", SceneRules.OneVideoLayer));
        CheckIds(scene.Layers.Select(layer => layer.Id), "layers", problems);

        CheckIds(scene.Segments.Select(segment => segment.Id), "segments", problems);
        for (var i = 0; i < scene.Segments.Count; i++)
            CheckSegment(scene.Segments[i], $"segments[{i}]", source, problems);

        return problems;
    }

    /// <summary>Does nothing for a valid scene.</summary>
    /// <exception cref="AppException">
    /// bad_request, with the broken fields under `scene.`, as the body of PUT …/scene names them
    /// </exception>
    public static void EnsureValid(Scene scene, MediaInfo source)
    {
        var problems = Validate(scene, source);
        if (problems.Count == 0) return;

        throw new AppException(
            ErrorCode.BadRequest,
            $"The scene breaks {problems.Count} rule(s): " +
            string.Join(", ", problems.Select(problem => $"{problem.Field} {problem.Rule}")))
        {
            Fields = problems
                .GroupBy(problem => $"scene.{problem.Field}")
                .ToDictionary(group => group.Key, group => group.Select(problem => problem.Rule).ToArray()),
        };
    }

    private static void CheckIds(IEnumerable<string> ids, string list, List<SceneProblem> problems)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var id in ids)
        {
            if (string.IsNullOrWhiteSpace(id)) problems.Add(new SceneProblem($"{list}[{index}].id", SceneRules.Empty));
            else if (!seen.Add(id)) problems.Add(new SceneProblem($"{list}[{index}].id", SceneRules.Duplicate));
            index++;
        }
    }

    private static void CheckSegment(Segment segment, string field, MediaInfo source, List<SceneProblem> problems)
    {
        var frames = source.FrameCount;

        if (segment.InFrame < 0 || segment.InFrame >= frames)
            problems.Add(new SceneProblem($"{field}.inFrame", SceneRules.OutOfRange));

        if (segment.OutFrame < segment.InFrame)
            problems.Add(new SceneProblem($"{field}.outFrame", SceneRules.BeforeIn));
        else if (segment.OutFrame >= frames)
            problems.Add(new SceneProblem($"{field}.outFrame", SceneRules.OutOfRange));

        CheckExcluded(segment, field, problems);

        // NaN fails both comparisons, so it is out of range too
        if (!(segment.Speed >= Segment.MinSpeed && segment.Speed <= Segment.MaxSpeed))
            problems.Add(new SceneProblem($"{field}.speed", SceneRules.OutOfRange));

        switch (segment.Frame)
        {
            case SegmentFrame.Crop crop:
                CheckCrop(crop.Rect, $"{field}.frame.rect", source, problems);
                break;
            case SegmentFrame.Stretch stretch:
                if (!(stretch.Aspect.Width > 0 && stretch.Aspect.Height > 0) ||
                    !double.IsFinite(stretch.Aspect.Width) || !double.IsFinite(stretch.Aspect.Height))
                    problems.Add(new SceneProblem($"{field}.frame.aspect", SceneRules.NotPositive));
                break;
        }
    }

    private static void CheckExcluded(Segment segment, string field, List<SceneProblem> problems)
    {
        var excluded = new HashSet<int>();
        for (var i = 0; i < segment.ExcludedFrames.Count; i++)
        {
            var frame = segment.ExcludedFrames[i];
            if (frame < segment.InFrame || frame > segment.OutFrame)
                problems.Add(new SceneProblem($"{field}.excludedFrames[{i}]", SceneRules.OutOfRange));
            else if (!excluded.Add(frame))
                problems.Add(new SceneProblem($"{field}.excludedFrames[{i}]", SceneRules.Duplicate));
        }

        if (segment.OutFrame >= segment.InFrame && excluded.Count == segment.OutFrame - segment.InFrame + 1)
            problems.Add(new SceneProblem($"{field}.excludedFrames", SceneRules.NoFramesLeft));
    }

    private static void CheckCrop(FrameRect rect, string field, MediaInfo source, List<SceneProblem> problems)
    {
        if (!IsWhole(rect.X) || !IsWhole(rect.Y) || !IsWhole(rect.Width) || !IsWhole(rect.Height))
            problems.Add(new SceneProblem(field, SceneRules.NotWholePixels));
        else if (rect.Width <= 0 || rect.Height <= 0)
            problems.Add(new SceneProblem(field, SceneRules.Empty));
        else if (rect.X < 0 || rect.Y < 0 || rect.X + rect.Width > source.Width || rect.Y + rect.Height > source.Height)
            problems.Add(new SceneProblem(field, SceneRules.OutsideSource));
    }

    private static bool IsWhole(double value) => double.IsFinite(value) && Math.Floor(value) == value;
}
