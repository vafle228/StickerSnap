namespace StickerSnap.Core.Projects;

/// <summary>A rule of the scene that a field breaks, for a bad_request's `fields`.</summary>
/// <param name="Field">The field's path in the scene: `segments[0].outFrame`</param>
/// <param name="Rule">One of <see cref="SceneRules"/>: `out_of_range`</param>
public sealed record SceneProblem(string Field, string Rule);

/// <summary>What <see cref="SceneValidator"/> can say about a field, in the words of `fields`.</summary>
public static class SceneRules
{
    /// <summary>A schemaVersion this server does not know.</summary>
    public const string Unsupported = "unsupported";

    /// <summary>The MVP draws a scene from exactly one video layer.</summary>
    public const string OneVideoLayer = "one_video_layer";

    /// <summary>An id that is blank, or a crop with no area.</summary>
    public const string Empty = "empty";

    /// <summary>An id another layer or segment already has, or a frame excluded twice.</summary>
    public const string Duplicate = "duplicate";

    /// <summary>Outside the source's frames, or a speed outside 0.25–4.</summary>
    public const string OutOfRange = "out_of_range";

    /// <summary>The Out frame comes before the In frame.</summary>
    public const string BeforeIn = "before_in";

    /// <summary>Every frame of the segment is excluded, so nothing plays.</summary>
    public const string NoFramesLeft = "no_frames_left";

    /// <summary>A crop between pixels: FFmpeg crops whole ones.</summary>
    public const string NotWholePixels = "not_whole_pixels";

    /// <summary>A crop that reaches past the edge of the picture.</summary>
    public const string OutsideSource = "outside_source";

    /// <summary>A stretch to a proportion with a side of zero or less.</summary>
    public const string NotPositive = "not_positive";
}
