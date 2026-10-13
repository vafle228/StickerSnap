using System.Text.Json.Serialization;
using StickerSnap.Api.Json;
using StickerSnap.Application.App;
using StickerSnap.Application.Projects;
using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;
using Domain = StickerSnap.Core.Projects;

namespace StickerSnap.Api.Contracts;

/// <summary>A card of «Проекты»: GET /api/v1/projects, PATCH /api/v1/projects/{id}, event projectChanged.</summary>
/// <param name="UpdatedAt">«12 минут назад» is the SPA's</param>
/// <param name="DurationSec">The chip on the cover: the longest segment</param>
public sealed record ProjectSummary(
    Guid Id,
    string Name,
    string? Path,
    DateTimeOffset UpdatedAt,
    int SegmentCount,
    CardPreset Preset,
    double DurationSec,
    string CoverUrl,
    PresenceStatus SourceStatus)
{
    public static ProjectSummary From(ProjectListItem item) => new(
        item.Card.Id,
        item.Card.Name,
        item.Card.Path,
        item.Card.UpdatedAt,
        item.Card.SegmentCount,
        item.Card.Preset,
        item.Card.DurationSec,
        ProjectUrls.Cover(item.Card.Id),
        item.SourceStatus);
}

/// <summary>A whole project (schema Project): GET /api/v1/projects/{id}, POST /api/v1/projects/open.</summary>
/// <param name="Path">The .ssproj</param>
/// <param name="MediaUrl">For the editor's &lt;video&gt;</param>
/// <param name="Exports">The last export of each segment by its id; stale once the segment was edited since</param>
public sealed record Project(
    Guid Id,
    string Name,
    string? Path,
    int Revision,
    DateTimeOffset SavedAt,
    SourceInfo Source,
    string MediaUrl,
    Scene Scene,
    IReadOnlyDictionary<string, ExportResult> Exports)
{
    public static Project From(OpenedProject opened, AppMode mode)
    {
        var project = opened.Project;
        return new Project(
            project.Id,
            project.Name,
            opened.Path,
            project.Revision,
            project.SavedAt,
            SourceOf(project.Source, opened.SourceStatus, mode),
            ProjectUrls.Media(project.Id),
            Scene.From(project.Scene),
            project.Exports.ToDictionary(
                pair => pair.Key, pair => ExportResult.From(pair.Value, pair.Value.IsStaleIn(project), mode), StringComparer.Ordinal));
    }

    /// <summary>Without its path in web mode: the browser has no use for the server's paths.</summary>
    private static SourceInfo SourceOf(ProjectSource source, PresenceStatus status, AppMode mode) =>
        SourceInfo.From(Core.Media.SourceInfo.From(source.Media, source.FileName, mode == AppMode.Desktop ? source.Path : null) with
        {
            Status = status,
        });
}

/// <summary>The project's video as the editor needs it (schema SourceInfo), from the Core's own.</summary>
/// <param name="Path">Not in web mode</param>
public sealed record SourceInfo(
    string FileName,
    string? Path,
    PresenceStatus Status,
    MediaFormat Format,
    string Codec,
    int Width,
    int Height,
    Rational Fps,
    int FrameCount,
    double DurationSec,
    bool HasAlpha,
    long SizeBytes)
{
    public static SourceInfo From(Core.Media.SourceInfo source) => new(
        source.FileName,
        source.Path,
        source.Status,
        source.Format,
        source.Codec,
        source.Width,
        source.Height,
        new Rational(source.Fps.Num, source.Fps.Den),
        source.FrameCount,
        source.DurationSec,
        source.HasAlpha,
        source.SizeBytes);
}

/// <summary>
/// fps 30000/1001 without a rounding error (schema Rational): a class, because the schema of a
/// struct's constructor leaves its fields optional.
/// </summary>
public sealed record Rational(int Num, int Den);

/// <summary>What the editor edits and the .ssproj keeps (schema Scene).</summary>
public sealed record Scene(
    [property: Const(Domain.Scene.CurrentSchemaVersion)] int SchemaVersion,
    IReadOnlyList<VideoLayer> Layers,
    IReadOnlyList<Segment> Segments)
{
    public static Scene From(Domain.Scene scene) => new(
        scene.SchemaVersion,
        [.. scene.Layers.Select(VideoLayer.From)],
        [.. scene.Segments.Select(Segment.From)]);

    public Domain.Scene ToScene() => new(
        SchemaVersion,
        [.. Layers.Select(layer => layer.ToLayer())],
        [.. Segments.Select(segment => segment.ToSegment())]);
}

/// <summary>The source video as a layer of the scene (schema VideoLayer).</summary>
public sealed record VideoLayer(string Id, LayerKind Kind)
{
    public static VideoLayer From(Layer layer) => layer switch
    {
        Layer.Video video => new VideoLayer(video.Id, LayerKind.Video),
        _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "A layer the contract does not know"),
    };

    public Layer ToLayer() => new Layer.Video(Id);
}

/// <summary>The only kind of layer in the MVP.</summary>
public enum LayerKind
{
    Video,
}

/// <summary>One sticker cut from the source (schema Segment).</summary>
public sealed record Segment(
    string Id,
    string Name,
    int InFrame,
    int OutFrame,
    IReadOnlyList<int> ExcludedFrames,
    SegmentFrame Frame,
    double Speed,
    LoopMode Loop,
    SegmentPreset Preset,
    bool LongSticker,
    bool AutoFit)
{
    public static Segment From(Domain.Segment segment) => new(
        segment.Id,
        segment.Name,
        segment.InFrame,
        segment.OutFrame,
        segment.ExcludedFrames,
        SegmentFrame.From(segment.Frame),
        segment.Speed,
        segment.Loop,
        segment.Preset,
        segment.LongSticker,
        segment.AutoFit);

    public Domain.Segment ToSegment() => new(
        Id, Name, InFrame, OutFrame, [.. ExcludedFrames], Frame.ToFrame(), Speed, Loop, Preset, LongSticker, AutoFit);
}

/// <summary>R3, «Кадр»: a crop or a stretch, told apart by `mode` (schemas Crop and Stretch).</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "mode")]
[JsonDerivedType(typeof(Crop), "crop")]
[JsonDerivedType(typeof(Stretch), "stretch")]
public abstract record SegmentFrame
{
    public static SegmentFrame From(Domain.SegmentFrame frame) => frame switch
    {
        Domain.SegmentFrame.Crop crop => new Crop(crop.Preset, new Rect(crop.Rect.X, crop.Rect.Y, crop.Rect.Width, crop.Rect.Height)),
        Domain.SegmentFrame.Stretch stretch => new Stretch(stretch.Preset, new Aspect(stretch.Aspect.Width, stretch.Aspect.Height)),
        _ => throw new ArgumentOutOfRangeException(nameof(frame), frame, "A frame the contract does not know"),
    };

    public abstract Domain.SegmentFrame ToFrame();
}

/// <param name="Rect">In the source's pixels</param>
public sealed record Crop(CropPreset Preset, Rect Rect) : SegmentFrame
{
    public override Domain.SegmentFrame ToFrame() =>
        new Domain.SegmentFrame.Crop(Preset, new FrameRect(Rect.X, Rect.Y, Rect.Width, Rect.Height));
}

/// <param name="Aspect">Parts; the longer side becomes 512</param>
public sealed record Stretch(StretchPreset Preset, Aspect Aspect) : SegmentFrame
{
    public override Domain.SegmentFrame ToFrame() =>
        new Domain.SegmentFrame.Stretch(Preset, new FrameAspect(Aspect.Width, Aspect.Height));
}

public sealed record Rect(double X, double Y, double Width, double Height);

public sealed record Aspect(double Width, double Height);

/// <summary>Event projectRemoved (schema ProjectRemovedEvent).</summary>
public sealed record ProjectRemovedEvent(Guid Id);

/// <summary>POST /api/v1/projects/open (schema OpenProjectRequest).</summary>
/// <param name="Path">The .ssproj</param>
public sealed record OpenProjectRequest(string Path);

/// <summary>PATCH /api/v1/projects/{id} (schema RenameProjectRequest).</summary>
public sealed record RenameProjectRequest(string Name);

/// <summary>PUT /api/v1/projects/{id}/scene (schema SaveSceneRequest): the whole scene over the revision last seen.</summary>
public sealed record SaveSceneRequest(int Revision, Scene Scene);

/// <summary>The answer to PUT …/scene (schema SaveSceneResponse).</summary>
public sealed record SaveSceneResponse(int Revision, DateTimeOffset SavedAt)
{
    public static SaveSceneResponse From(SavedScene saved) => new(saved.Revision, saved.SavedAt);
}

/// <summary>The answer to PUT …/source (schema ReplaceSourceResponse).</summary>
/// <param name="SegmentsOutOfRange">The segments past the new video's end; empty when there is nothing to warn about</param>
public sealed record ReplaceSourceResponse(Project Project, IReadOnlyList<string> SegmentsOutOfRange)
{
    public static ReplaceSourceResponse From(ReplacedSource replaced, AppMode mode) =>
        new(Project.From(replaced.Opened, mode), replaced.SegmentsOutOfRange);
}

/// <summary>The addresses the browser loads by itself; the SPA adds the token to them.</summary>
public static class ProjectUrls
{
    public static string Cover(Guid id) => $"/api/v1/projects/{id}/cover";

    public static string Media(Guid id) => $"/api/v1/projects/{id}/media";
}
