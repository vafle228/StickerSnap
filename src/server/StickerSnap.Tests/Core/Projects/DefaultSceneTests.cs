using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;

namespace StickerSnap.Tests.Core.Projects;

/// <summary>The scene a new project opens with, across the videos people drop: rates, lengths, shapes.</summary>
public sealed class DefaultSceneTests
{
    private static MediaInfo Video(int width, int height, Rational fps, int frames) => new(
        MediaFormat.Mp4, "h264", width, height, fps, IsVariableFrameRate: false,
        frames, DurationSec: frames / fps.ToDouble(), HasAlpha: false, HasAudio: false, SizeBytes: 1_000_000);

    public static TheoryData<string, int, int, Rational, int, int> Lengths => new()
    {
        // what, width, height, fps, frames, the Out frame
        { "8 s at 30 fps: the first 3 s", 1920, 1080, new Rational(30, 1), 240, 89 },
        { "a 1.5 s GIF at 12 fps: all of it", 480, 270, new Rational(12, 1), 18, 17 },
        { "29.97 fps: 89 frames, 2.97 s, not 90 over 3 s", 1920, 1080, new Rational(30000, 1001), 300, 88 },
        { "exactly 3 s: all of it", 640, 360, new Rational(25, 1), 75, 74 },
        { "one frame", 512, 512, new Rational(10, 1), 1, 0 },
        { "a rate nobody knows: all of it", 640, 360, new Rational(0, 0), 40, 39 },
    };

    [Theory]
    [MemberData(nameof(Lengths))]
    public void Takes_whole_frames_within_3_seconds_from_the_start(string what, int width, int height, Rational fps, int frames, int outFrame)
    {
        var segment = DefaultScene.For(Video(width, height, fps, frames)).Segments.Single();

        segment.InFrame.Should().Be(0, what);
        segment.OutFrame.Should().Be(outFrame, what);
    }

    public static TheoryData<int, int, CropPreset, FrameRect> Shapes => new()
    {
        { 1920, 1080, CropPreset.Square, new FrameRect(420, 0, 1080, 1080) },
        { 720, 1280, CropPreset.Square, new FrameRect(0, 280, 720, 720) },
        { 512, 512, CropPreset.Source, new FrameRect(0, 0, 512, 512) },
        // An odd margin: the extra pixel stays on the right, the crop stays in whole pixels
        { 641, 360, CropPreset.Square, new FrameRect(140, 0, 360, 360) },
        { 99, 100, CropPreset.Square, new FrameRect(0, 0, 99, 99) },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Crops_the_middle_square_or_keeps_a_square_source_whole(int width, int height, CropPreset preset, FrameRect rect)
    {
        var segment = DefaultScene.For(Video(width, height, new Rational(30, 1), 90)).Segments.Single();

        segment.Frame.Should().Be(new SegmentFrame.Crop(preset, rect));
    }

    [Fact]
    public void Makes_one_sticker_with_the_editor_s_defaults()
    {
        var scene = DefaultScene.For(Video(1920, 1080, new Rational(30, 1), 240));

        scene.SchemaVersion.Should().Be(Scene.CurrentSchemaVersion);
        scene.Layers.Should().Equal(new Layer.Video("layer-1"));
        var segment = scene.Segments.Single();
        segment.Id.Should().Be("seg-1");
        segment.Name.Should().Be("Стикер 1");
        segment.ExcludedFrames.Should().BeEmpty();
        segment.Speed.Should().Be(1);
        segment.Loop.Should().Be(LoopMode.Normal);
        segment.Preset.Should().Be(SegmentPreset.Sticker);
        segment.LongSticker.Should().BeFalse();
        segment.AutoFit.Should().BeTrue();
    }

    [Theory]
    [InlineData(1920, 1080, 30, 240)]
    [InlineData(641, 361, 12, 18)]
    [InlineData(100, 100, 30, 1)]
    public void Keeps_every_rule_of_the_scene(int width, int height, int fps, int frames)
    {
        var source = Video(width, height, new Rational(fps, 1), frames);

        SceneValidator.Validate(DefaultScene.For(source), source).Should().BeEmpty();
    }

    [Fact]
    public void Refuses_a_video_without_frames()
    {
        var make = () => DefaultScene.For(Video(640, 360, new Rational(30, 1), 0));

        make.Should().Throw<ArgumentOutOfRangeException>();
    }
}
