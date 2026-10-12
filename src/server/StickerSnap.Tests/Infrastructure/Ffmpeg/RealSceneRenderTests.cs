using StickerSnap.Application.Media;
using StickerSnap.Application.Render;
using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Render;
using StickerSnap.Core.Validation;
using StickerSnap.Infrastructure.Ffmpeg;
using StickerSnap.Tests.TestData;

namespace StickerSnap.Tests.Infrastructure.Ffmpeg;

/// <summary>
/// Segments compiled by <see cref="SceneCompiler"/> and played by the real FFmpeg: the frames
/// picked are the ones the editor numbers, the file has the frames, size and length the spec
/// promised, and a compiled scene of the test corpus fits Telegram through the ladder.
/// </summary>
[Trait("Category", Category.Slow)]
public sealed class RealSceneRenderTests(RealFfmpeg ffmpeg) : IClassFixture<RealFfmpeg>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static readonly Segment Segment = new(
        "seg-1", "Стикер 1", 0, 0, [], new SegmentFrame.Crop(CropPreset.Square, new FrameRect(40, 0, 240, 240)),
        Speed: 1, LoopMode.Normal, SegmentPreset.Sticker, LongSticker: false, AutoFit: true);

    private async Task<(string Path, MediaInfo Media)> VideoAsync(string rate = "30")
    {
        var name = $"video-{rate.Replace('/', '_')}.mp4";
        var path = File.Exists(ffmpeg.Clip(name))
            ? ffmpeg.Clip(name)
            : await ffmpeg.MakeAsync(name, "-f", "lavfi", "-i", $"testsrc2=s=320x240:r={rate}:d=5", "-c:v", "libopenh264", "-b:v", "2M");
        return (path, await ffmpeg.Probe.ProbeAsync(path, Cancel));
    }

    private static StickerRender Render(string path, MediaInfo source, Segment segment, string output = "out.webm") => new(
        path,
        source,
        SceneCompiler.Compile(new Scene(Scene.CurrentSchemaVersion, [new Layer.Video("video")], [segment]), segment.Id, source),
        new RateControl.ConstantQuality(40),
        RenderEffort.Draft,
        output);

    /// <summary>A hash of each frame the graph lets out, in order.</summary>
    private async Task<IReadOnlyList<string>> FramesAsync(StickerRender render)
    {
        var result = await ffmpeg.Ffmpeg.RunAsync(
            [.. StickerFilterGraph.Input(render), "-filter_complex", StickerFilterGraph.Filters(render), "-map", StickerFilterGraph.Output, "-f", "framemd5", "-"],
            progress: null,
            Cancel);
        result.IsSuccess.Should().BeTrue(result.Log);
        return result.Output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.StartsWith('#'))
            .Select(line => line.Split(',')[^1].Trim())
            .ToList();
    }

    /// <summary>
    /// The same frames, read once from a time half a frame before the first and once frame by
    /// frame from the start (the source taken for one with a variable rate, which is never sought).
    /// </summary>
    [Theory]
    [InlineData("30", 40, 99)]
    [InlineData("30000/1001", 37, 120)]
    [InlineData("30", 1, 30)]
    public async Task Seeks_to_exactly_the_first_frame_of_the_segment(string rate, int inFrame, int outFrame)
    {
        var (path, source) = await VideoAsync(rate);
        var segment = Segment with { InFrame = inFrame, OutFrame = outFrame };

        var sought = await FramesAsync(Render(path, source, segment));
        var counted = await FramesAsync(Render(path, source with { IsVariableFrameRate = true }, segment));

        StickerFilterGraph.Input(Render(path, source, segment)).Should().Contain("-ss");
        sought.Should().HaveCount(outFrame - inFrame + 1).And.Equal(counted);
        sought.Should().OnlyHaveUniqueItems("testsrc2 draws every frame anew");
    }

    [Fact]
    public async Task Leaves_the_excluded_frames_out()
    {
        var (path, source) = await VideoAsync();
        var segment = Segment with { InFrame = 20, OutFrame = 39 };

        var all = await FramesAsync(Render(path, source, segment));
        var some = await FramesAsync(Render(path, source, segment with { ExcludedFrames = [20, 23, 30, 31, 39] }));

        some.Should().Equal(all.Where((_, i) => i is not (0 or 3 or 10 or 11 or 19)));
    }

    [Fact]
    public async Task Plays_a_ping_pong_back_without_its_two_ends()
    {
        var (path, source) = await VideoAsync();
        var render = Render(path, source, Segment with { InFrame = 10, OutFrame = 29, Loop = LoopMode.Pingpong });

        var frames = await FramesAsync(render);

        frames.Should().HaveCount(38);
        frames.Skip(20).Should().Equal(frames.Take(19).Skip(1).Reverse());
    }

    public static TheoryData<string, Segment> Scenes => new()
    {
        { "half speed", Segment with { OutFrame = 149, Speed = 0.5 } },
        { "four times as fast", Segment with { OutFrame = 149, Speed = 4 } },
        { "a quarter speed", Segment with { InFrame = 100, OutFrame = 119, Speed = 0.25 } },
        { "a ping-pong at 1.5×", Segment with { InFrame = 50, OutFrame = 149, Speed = 1.5, Loop = LoopMode.Pingpong } },
        { "an odd crop", Segment with { OutFrame = 59, Frame = new SegmentFrame.Crop(CropPreset.Free, new FrameRect(3, 5, 241, 181)) } },
        { "a stretch to 21:9", Segment with { OutFrame = 59, Frame = new SegmentFrame.Stretch(StretchPreset.Free, new FrameAspect(21, 9)) } },
        { "an emoji", Segment with { InFrame = 30, OutFrame = 89, Preset = SegmentPreset.Emoji } },
        { "excluded frames", Segment with { InFrame = 60, OutFrame = 89, ExcludedFrames = [61, 62, 70] } },
    };

    [Theory]
    [MemberData(nameof(Scenes))]
    public async Task Renders_the_frames_size_and_length_the_spec_promised(string name, Segment segment)
    {
        var (path, source) = await VideoAsync();
        var render = Render(path, source, segment, ffmpeg.Clip($"{name}.webm"));

        var file = await new FfmpegStickerRenderer(ffmpeg.Ffmpeg).RenderAsync(render, progress: null, Cancel);
        var sticker = await ffmpeg.Probe.ProbeAsync(file.Path, Cancel);

        var spec = render.Spec;
        sticker.Should().BeEquivalentTo(new { spec.Width, spec.Height, FrameCount = spec.PlayedFrames, Fps = new Rational(spec.Fps, 1) }, name);
        sticker.DurationSec.Should().BeApproximately(spec.DurationSec, 0.04, name);
        spec.DurationSec.Should().BeLessThanOrEqualTo(3);
    }

    /// <summary>A transparent sticker cut from the middle, slowed and played back, and an emoji.</summary>
    [Theory]
    [InlineData("Corpus/22-sticker-cat.gif", 5, 40, 0.75, LoopMode.Pingpong, SegmentPreset.Sticker)]
    [InlineData("Corpus/05-dance.gif", 0, 200, 1.5, LoopMode.Normal, SegmentPreset.Sticker)]
    [InlineData("Corpus/26-sticker-heart.gif", 0, 200, 1, LoopMode.Pingpong, SegmentPreset.Emoji)]
    public async Task Fits_a_compiled_scene_of_the_corpus_into_a_sticker_Telegram_takes(
        string clip, int inFrame, int outFrame, double speed, LoopMode loop, SegmentPreset preset)
    {
        var path = await TestFiles.CopyToAsync(clip, Directory.CreateDirectory(ffmpeg.Clip("corpus")).FullName, Cancel);
        var source = await ffmpeg.Probe.ProbeAsync(path, Cancel);
        var side = Math.Min(source.Width, source.Height);
        var segment = Segment with
        {
            InFrame = inFrame,
            OutFrame = Math.Min(outFrame, source.FrameCount - 1),
            Frame = new SegmentFrame.Crop(CropPreset.Square, new FrameRect((source.Width - side) / 2, (source.Height - side) / 2, side, side)),
            Speed = speed,
            Loop = loop,
            Preset = preset,
        };
        var spec = SceneCompiler.Compile(new Scene(Scene.CurrentSchemaVersion, [new Layer.Video("video")], [segment]), segment.Id, source);
        var limits = SceneCompiler.LimitsOf(preset);
        var renderer = new FfmpegStickerRenderer(ffmpeg.Ffmpeg);

        var fitted = await new StickerFitService(renderer).FitAsync(
            new StickerFit(path, source, spec, limits, LadderProfile.Sticker, ffmpeg.Clip($"{Path.GetFileName(clip)}.{preset}.webm")), progress: null, Cancel);
        var sticker = await ffmpeg.Probe.ProbeAsync(fitted.Path, Cancel);

        StickerValidator.Validate(sticker, preset, limits).Problems.Should().BeEmpty();
        sticker.FrameCount.Should().Be(spec.At(fitted.Rung).PlayedFrames);
        sticker.HasAlpha.Should().Be(source.HasAlpha);
    }
}
