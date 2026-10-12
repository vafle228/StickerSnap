using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Render;
using StickerSnap.Core.Telegram;
using StickerSnap.Tests.Core.Projects;

namespace StickerSnap.Tests.Core.Render;

public sealed class SceneCompilerTests
{
    private static readonly PresetLimits Sticker = TelegramLimits.Current.Sticker;

    /// <summary>1920×1080 at 30 fps, 150 frames.</summary>
    private static readonly MediaInfo Video = Media.Landscape;

    private static readonly Segment Whole = Scenes.Segment with
    {
        InFrame = 0,
        OutFrame = 89,
        Frame = new SegmentFrame.Crop(CropPreset.Square, new FrameRect(420, 0, 1080, 1080)),
    };

    private static StickerSpec Compile(Segment segment, MediaInfo? source = null) =>
        SceneCompiler.Compile(segment, source ?? Video, SceneCompiler.LimitsOf(segment.Preset));

    private static Segment Cropped(double x, double y, double width, double height) =>
        Whole with { Frame = new SegmentFrame.Crop(CropPreset.Free, new FrameRect(x, y, width, height)) };

    private static Segment Stretched(StretchPreset preset, double width, double height) =>
        Whole with { Frame = new SegmentFrame.Stretch(preset, new FrameAspect(width, height)) };

    [Fact]
    public void Plays_the_frames_from_in_to_out_at_the_rate_of_the_source()
    {
        var spec = Compile(Whole with { InFrame = 30, OutFrame = 89 });

        spec.Clip.Should().Be(new StickerClip(new SourceFrames(30, 89, []), 1, false, 2, 3));
        (spec.Fps, spec.ForwardFrames, spec.DurationSec).Should().Be((30, 60, 2));
    }

    [Fact]
    public void Keeps_the_cropped_rectangle_and_scales_it_to_512_px()
    {
        var spec = Compile(Whole);

        (spec.Crop, spec.Size).Should().Be((new PixelRect(420, 0, 1080, 1080), new PixelSize(512, 512)));
    }

    [Theory]
    [InlineData(1920, 1080, 512, 288)]
    [InlineData(1080, 1920, 288, 512)]
    [InlineData(241, 181, 512, 384)]
    [InlineData(181, 241, 384, 512)]
    [InlineData(1920, 822, 512, 220)]
    [InlineData(1920, 3, 512, 2)]
    [InlineData(511, 512, 512, 512)]
    public void Gives_the_longer_side_512_px_and_keeps_both_sides_even(int width, int height, int stickerWidth, int stickerHeight)
    {
        var source = Video with { Width = Math.Max(width, 1920), Height = Math.Max(height, 1920) };

        Compile(Cropped(0, 0, width, height), source).Size.Should().Be(new PixelSize(stickerWidth, stickerHeight));
    }

    [Fact]
    public void Stretches_the_whole_picture_to_the_proportion_asked()
    {
        var squashed = Compile(Stretched(StretchPreset.Squash, 1, 1));
        var wide = Compile(Stretched(StretchPreset.Free, 21, 9));

        (squashed.Crop, squashed.Size).Should().Be((new PixelRect(0, 0, 1920, 1080), new PixelSize(512, 512)));
        (wide.Crop, wide.Size).Should().Be((new PixelRect(0, 0, 1920, 1080), new PixelSize(512, 220)));
    }

    [Fact]
    public void Makes_a_custom_emoji_100_px_square_whatever_the_frame()
    {
        var cropped = Compile(Cropped(0, 0, 1920, 1080) with { Preset = SegmentPreset.Emoji });
        var stretched = Compile(Stretched(StretchPreset.Source, 16, 9) with { Preset = SegmentPreset.Emoji });

        cropped.Size.Should().Be(new PixelSize(100, 100));
        stretched.Size.Should().Be(new PixelSize(100, 100));
    }

    [Fact]
    public void Cuts_a_long_segment_at_3_seconds()
    {
        var spec = Compile(Whole with { OutFrame = 149 });

        spec.Clip.Frames.Should().Be(new SourceFrames(0, 89, []));
        spec.DurationSec.Should().Be(3);
    }

    [Fact]
    public void Plays_a_slowed_segment_at_the_rate_its_frames_come_at_and_cuts_it_at_3_seconds()
    {
        var spec = Compile(Whole with { Speed = 0.5 });

        spec.Fps.Should().Be(15);
        spec.Clip.Frames.Should().Be(new SourceFrames(0, 44, []));
        (spec.ForwardFrames, spec.DurationSec).Should().Be((45, 3));
    }

    [Fact]
    public void Plays_a_segment_4_times_as_fast_in_a_quarter_of_its_time_at_30_fps()
    {
        var spec = Compile(Whole with { OutFrame = 149, Speed = 4 });

        spec.Fps.Should().Be(30);
        spec.Clip.Frames.Should().Be(new SourceFrames(0, 149, []));
        (spec.ForwardFrames, spec.DurationSec).Should().Be((38, 38 / 30.0));
    }

    [Fact]
    public void Plays_a_quarter_speed_segment_at_most_3_seconds()
    {
        var spec = Compile(Whole with { Speed = 0.25 }, Video with { Fps = new Rational(10, 1) });

        spec.Fps.Should().Be(30);
        spec.Clip.Frames.Should().Be(new SourceFrames(0, 7, []));
        (spec.ForwardFrames, spec.DurationSec).Should().Be((90, 3));
    }

    /// <summary>A ping-pong of n frames plays 2n − 2 (fit spike, open question 12).</summary>
    [Fact]
    public void Cuts_a_ping_pong_so_both_ways_play_within_3_seconds()
    {
        var spec = Compile(Whole with { Loop = LoopMode.Pingpong });

        spec.Clip.Frames.Should().Be(new SourceFrames(0, 45, []));
        (spec.ForwardFrames, spec.PlayedFrames, spec.DurationSec).Should().Be((46, 90, 3));
    }

    [Fact]
    public void Takes_a_frame_more_of_a_29_97_fps_video_to_fill_3_seconds_at_30()
    {
        var spec = Compile(Whole with { OutFrame = 149 }, Video with { Fps = new Rational(30000, 1001) });

        spec.Clip.Frames.Should().Be(new SourceFrames(0, 89, []));
        (spec.Fps, spec.ForwardFrames, spec.DurationSec).Should().Be((30, 90, 3));
    }

    [Fact]
    public void Leaves_the_excluded_frames_out_of_the_length()
    {
        var spec = Compile(Whole with { InFrame = 0, OutFrame = 9, ExcludedFrames = [5, 3] });

        spec.Clip.Frames.Should().Be(new SourceFrames(0, 9, [3, 5]));
        spec.ForwardFrames.Should().Be(8);
    }

    [Fact]
    public void Plays_as_many_frames_more_as_are_excluded_before_the_cut()
    {
        var spec = Compile(Whole with { OutFrame = 149, ExcludedFrames = [10, 11, 120] });

        spec.Clip.Frames.Should().Be(new SourceFrames(0, 91, [10, 11]));
        spec.DurationSec.Should().Be(3);
    }

    [Fact]
    public void Keeps_the_alpha_of_a_transparent_source()
    {
        Compile(Whole, Video with { HasAlpha = true }).KeepAlpha.Should().BeTrue();
        Compile(Whole).KeepAlpha.Should().BeFalse();
    }

    [Fact]
    public void Counts_the_rate_of_a_source_without_one_from_its_frames_and_length()
    {
        var spec = Compile(Whole, Video with { Fps = new Rational(0, 0), FrameCount = 125, DurationSec = 5 });

        spec.Fps.Should().Be(30);
        spec.Clip.Frames.Should().Be(new SourceFrames(0, 74, []));
        spec.DurationSec.Should().Be(3);
    }

    [Fact]
    public void Compiles_any_segment_of_a_scene_by_its_id()
    {
        var second = Whole with { Id = "seg-2", Preset = SegmentPreset.Emoji };
        var scene = Scenes.Of(Whole, second);

        SceneCompiler.Compile(scene, "seg-2", Video).Size.Should().Be(new PixelSize(100, 100));
        SceneCompiler.Compile(scene, "seg-1", Video).Size.Should().Be(new PixelSize(512, 512));
    }

    [Fact]
    public void Refuses_a_segment_the_scene_does_not_have()
    {
        var compile = () => SceneCompiler.Compile(Scenes.Of(Whole), "seg-9", Video);

        compile.Should().Throw<AppException>().Which.Code.Should().Be(ErrorCode.BadRequest);
    }

    [Fact]
    public void Refuses_a_segment_that_runs_past_the_end_of_the_source()
    {
        var compile = () => SceneCompiler.Compile(Scenes.Of(Whole with { OutFrame = 150 }), "seg-1", Video);

        compile.Should().Throw<AppException>().Which.Fields.Should().ContainKey("scene.segments[0].outFrame");
    }

    [Fact]
    public void Compiles_a_valid_segment_beside_one_past_the_end_of_a_replaced_source()
    {
        var scene = Scenes.Of(Whole, Whole with { Id = "seg-2", OutFrame = 400 });

        SceneCompiler.Compile(scene, "seg-1", Video).Clip.Frames.Should().Be(new SourceFrames(0, 89, []));
    }
}
