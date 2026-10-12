using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;

namespace StickerSnap.Infrastructure.Projects;

/// <summary>
/// The .ssproj format (contract, «Файл .ssproj»): indented JSON with its own schemaVersion. A file
/// of an older version is brought up to the current one by <see cref="Migration"/>s, one version
/// at a time, before it is read; a newer one is refused, so an old app never cuts what it does not
/// know out of a file. Only the shape is checked here: the scene's rules against its source are
/// <see cref="SceneValidator"/>'s, when the scene is saved.
/// </summary>
internal sealed class ProjectFile
{
    public const string Extension = ".ssproj";

    private const string VersionProperty = "schemaVersion";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
        WriteIndented = true,
        // Names and paths in Cyrillic stay readable: the file is JSON on a disk, never HTML
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // A missing or null property is a broken file, not a zero or an empty name
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        // Someone who edits the file by hand may put `mode` after `rect`
        AllowOutOfOrderMetadataProperties = true,
    };

    private readonly int _version;
    private readonly IReadOnlyDictionary<int, Migration> _migrations;

    /// <param name="version">The schemaVersion this server writes</param>
    /// <param name="migrations">By the version each one starts from: the one at 1 makes a file of version 2</param>
    public ProjectFile(int version, IReadOnlyDictionary<int, Migration> migrations)
    {
        for (var from = 1; from < version; from++)
            if (!migrations.ContainsKey(from))
                throw new ArgumentException($"No migration from schema {from} to {from + 1}", nameof(migrations));

        _version = version;
        _migrations = migrations;
    }

    /// <summary>Changes a file of one schemaVersion into the next one; the version itself is set after it.</summary>
    public delegate JsonObject Migration(JsonObject project);

    /// <summary>The format this server reads and writes: version 1, nothing to migrate yet.</summary>
    public static ProjectFile Current { get; } = new(1, new Dictionary<int, Migration>());

    public byte[] Write(Project project) => JsonSerializer.SerializeToUtf8Bytes(StoredProject.From(project, _version), Json);

    /// <param name="path">Only for the message</param>
    /// <exception cref="AppException">project_invalid or project_too_new</exception>
    public Project Read(ReadOnlySpan<byte> content, string path)
    {
        try
        {
            var root = JsonNode.Parse(content) as JsonObject
                       ?? throw new JsonException("Not a JSON object", "$", null, null);
            var version = VersionOf(root);
            if (version > _version)
                throw new AppException(
                    ErrorCode.ProjectTooNew,
                    $"{path} has schema {version}, this server knows up to {_version}")
                {
                    Log = $"$.{VersionProperty}: {version}, known up to {_version}",
                };

            for (; version < _version; version++)
            {
                root = _migrations[version](root);
                root[VersionProperty] = version + 1;
            }

            var stored = root.Deserialize<StoredProject>(Json) ?? throw new JsonException("Not a project", "$", null, null);
            return stored.ToProject();
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            var broken = Describe(exception);
            throw new AppException(ErrorCode.ProjectInvalid, $"{path} is not a project: {broken}", exception) { Log = broken };
        }
    }

    private static int VersionOf(JsonObject root) =>
        root[VersionProperty] is JsonValue value && value.TryGetValue<int>(out var version) && version >= 1
            ? version
            : throw new JsonException("Wants a schemaVersion of 1 or more", $"$.{VersionProperty}", null, null);

    /// <summary>
    /// `$.scene.segments[0].inFrame: The JSON value could not be converted to System.Int32.`:
    /// where the file breaks and how, without the line and byte, which count the migrated JSON
    /// rather than the file. JSON that does not parse has no property yet: its message keeps the
    /// line and byte of the file instead.
    /// </summary>
    private static string Describe(Exception exception)
    {
        const string pathMark = " Path: ";
        var message = exception.Message;
        var mark = message.IndexOf(pathMark, StringComparison.Ordinal);
        var path = exception is JsonException { Path: { } jsonPath } ? jsonPath : null;
        if (mark < 0) return path is null ? message : $"{path}: {message}";

        var reason = message[..mark];
        path ??= message[(mark + pathMark.Length)..].Split(" | ")[0].TrimEnd('.');
        return $"{path}: {reason}";
    }

    private static JsonException Broken(string path, string reason) => new(reason, path, null, null);

    // The file's shape, apart from the domain's: it outlives renames in the code. Every property is
    // required; one the format does not know is skipped, so a file a newer app wrote in the same
    // schema still opens.

    private sealed record StoredProject(
        int SchemaVersion,
        Guid Id,
        string Name,
        int Revision,
        DateTimeOffset SavedAt,
        StoredSource Source,
        StoredScene Scene,
        JsonObject Exports)
    {
        public static StoredProject From(Project project, int version) => new(
            version,
            project.Id,
            project.Name,
            project.Revision,
            project.SavedAt,
            StoredSource.From(project.Source),
            StoredScene.From(project.Scene),
            []);

        public Project ToProject() => new(Id, Name, Revision, SavedAt, Source.ToSource(), Scene.ToScene());
    }

    private sealed record StoredSource(string Path, string FileName, long SizeBytes, string Fingerprint, StoredMedia Media)
    {
        public static StoredSource From(ProjectSource source) => new(
            source.Path, source.FileName, source.Media.SizeBytes, source.Fingerprint, StoredMedia.From(source.Media));

        public ProjectSource ToSource() => new(Path, FileName, Fingerprint, Media.ToMedia(SizeBytes));
    }

    private sealed record StoredMedia(
        MediaFormat Format,
        string Codec,
        int Width,
        int Height,
        StoredRational Fps,
        bool VariableFrameRate,
        int FrameCount,
        double DurationSec,
        bool HasAlpha,
        bool HasAudio)
    {
        public static StoredMedia From(MediaInfo media) => new(
            media.Format,
            media.Codec,
            media.Width,
            media.Height,
            new StoredRational(media.Fps.Num, media.Fps.Den),
            media.IsVariableFrameRate,
            media.FrameCount,
            media.DurationSec,
            media.HasAlpha,
            media.HasAudio);

        public MediaInfo ToMedia(long sizeBytes) => new(
            Format,
            Codec,
            Width,
            Height,
            new Rational(Fps.Num, Fps.Den),
            VariableFrameRate,
            FrameCount,
            DurationSec,
            HasAlpha,
            HasAudio,
            sizeBytes);
    }

    private sealed record StoredRational(int Num, int Den);

    private sealed record StoredScene(int SchemaVersion, IReadOnlyList<StoredLayer> Layers, IReadOnlyList<StoredSegment> Segments)
    {
        public static StoredScene From(Scene scene) => new(
            scene.SchemaVersion,
            [.. scene.Layers.Select(StoredLayer.From)],
            [.. scene.Segments.Select(StoredSegment.From)]);

        public Scene ToScene() => new(
            SchemaVersion,
            [.. Layers.Select((layer, i) => (layer ?? throw Broken($"$.scene.layers[{i}]", "A layer is null")).ToLayer())],
            [.. Segments.Select((segment, i) => (segment ?? throw Broken($"$.scene.segments[{i}]", "A segment is null")).ToSegment())]);
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
    [JsonDerivedType(typeof(StoredVideoLayer), "video")]
    private abstract record StoredLayer
    {
        public static StoredLayer From(Layer layer) => layer switch
        {
            Layer.Video video => new StoredVideoLayer(video.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "A layer the format does not know"),
        };

        public abstract Layer ToLayer();
    }

    private sealed record StoredVideoLayer(string Id) : StoredLayer
    {
        public override Layer ToLayer() => new Layer.Video(Id);
    }

    private sealed record StoredSegment(
        string Id,
        string Name,
        int InFrame,
        int OutFrame,
        IReadOnlyList<int> ExcludedFrames,
        StoredFrame Frame,
        double Speed,
        LoopMode Loop,
        SegmentPreset Preset,
        bool LongSticker,
        bool AutoFit)
    {
        public static StoredSegment From(Segment segment) => new(
            segment.Id,
            segment.Name,
            segment.InFrame,
            segment.OutFrame,
            [.. segment.ExcludedFrames],
            StoredFrame.From(segment.Frame),
            segment.Speed,
            segment.Loop,
            segment.Preset,
            segment.LongSticker,
            segment.AutoFit);

        public Segment ToSegment() => new(
            Id,
            Name,
            InFrame,
            OutFrame,
            [.. ExcludedFrames],
            Frame.ToFrame(),
            Speed,
            Loop,
            Preset,
            LongSticker,
            AutoFit);
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "mode")]
    [JsonDerivedType(typeof(StoredCrop), "crop")]
    [JsonDerivedType(typeof(StoredStretch), "stretch")]
    private abstract record StoredFrame
    {
        public static StoredFrame From(SegmentFrame frame) => frame switch
        {
            SegmentFrame.Crop crop => new StoredCrop(
                crop.Preset, new StoredRect(crop.Rect.X, crop.Rect.Y, crop.Rect.Width, crop.Rect.Height)),
            SegmentFrame.Stretch stretch => new StoredStretch(
                stretch.Preset, new StoredAspect(stretch.Aspect.Width, stretch.Aspect.Height)),
            _ => throw new ArgumentOutOfRangeException(nameof(frame), frame, "A frame the format does not know"),
        };

        public abstract SegmentFrame ToFrame();
    }

    private sealed record StoredCrop(CropPreset Preset, StoredRect Rect) : StoredFrame
    {
        public override SegmentFrame ToFrame() => new SegmentFrame.Crop(Preset, new FrameRect(Rect.X, Rect.Y, Rect.Width, Rect.Height));
    }

    private sealed record StoredStretch(StretchPreset Preset, StoredAspect Aspect) : StoredFrame
    {
        public override SegmentFrame ToFrame() => new SegmentFrame.Stretch(Preset, new FrameAspect(Aspect.Width, Aspect.Height));
    }

    private sealed record StoredRect(double X, double Y, double Width, double Height);

    private sealed record StoredAspect(double Width, double Height);
}
