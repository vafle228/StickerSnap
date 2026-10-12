using StickerSnap.Application.Render;
using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Render;
using StickerSnap.Core.Telegram;
using StickerSnap.Core.Validation;
using StickerSnap.Infrastructure.Ffmpeg;

namespace StickerSnap.Tests.Infrastructure.Ffmpeg;

/// <summary>
/// The render engine over the real FFmpeg, on clips FFmpeg makes with lavfi: a busy video, a
/// transparent one and a fast portrait one each give a sticker Telegram takes, as a final file
/// and as a draft, with the progress and the cancel the job queue relies on.
/// </summary>
[Trait("Category", Category.Slow)]
public sealed class RealSegmentRenderTests(RealFfmpeg ffmpeg) : IClassFixture<RealFfmpeg>
{
    private static readonly Segment Segment = new(
        "seg-1", "Стикер 1", 0, 0, [], new SegmentFrame.Stretch(StretchPreset.Free, new FrameAspect(1, 1)),
        Speed: 1, LoopMode.Normal, SegmentPreset.Sticker, LongSticker: false, AutoFit: true);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private SegmentRenderService Engine
    {
        get
        {
            var renderer = new FfmpegStickerRenderer(ffmpeg.Ffmpeg);
            return new SegmentRenderService(new StickerFitService(renderer), renderer, ffmpeg.Probe);
        }
    }

    /// <summary>The clips, each made once per run of the class.</summary>
    private Task<string> ClipAsync(string name) => File.Exists(ffmpeg.Clip(name)) ? Task.FromResult(ffmpeg.Clip(name)) : name switch
    {
        // Noise on every frame: the hardest picture there is for the 256 KB
        "busy.mp4" => ffmpeg.MakeAsync(name,
            "-f", "lavfi", "-i", "testsrc2=s=1280x720:r=30:d=4,noise=alls=30:allf=t+u", "-c:v", "libopenh264", "-b:v", "8M"),
        "transparent.webm" => ffmpeg.MakeAsync(name,
            "-f", "lavfi", "-i", "testsrc2=s=640x480:r=30:d=3,format=yuva420p,geq=lum='lum(X,Y)':cb='cb(X,Y)':cr='cr(X,Y)':a='255*gt(hypot(X-320,Y-240),100+60*sin(T*4))'",
            "-c:v", "libvpx-vp9", "-pix_fmt", "yuva420p", "-b:v", "8M"),
        "portrait.mp4" => ffmpeg.MakeAsync(name,
            "-f", "lavfi", "-i", "testsrc2=s=405x720:r=60:d=4", "-c:v", "libopenh264", "-pix_fmt", "yuv420p"),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    private async Task<SegmentRender> RequestAsync(string clip, Segment segment, RenderEffort quality)
    {
        var path = await ClipAsync(clip);
        var source = await ffmpeg.Probe.ProbeAsync(path, Cancel);
        segment = segment with { OutFrame = segment.OutFrame == 0 ? source.FrameCount - 1 : segment.OutFrame };
        var output = ffmpeg.Clip($"{clip}.{segment.Preset}.{segment.Loop}.{quality}.webm");
        return new SegmentRender(path, source, new Scene(Scene.CurrentSchemaVersion, [new Layer.Video("video")], [segment]), segment.Id, quality, output);
    }

    public static TheoryData<string, Segment> Clips => new()
    {
        { "busy.mp4", Segment },
        { "transparent.webm", Segment with { Frame = new SegmentFrame.Crop(CropPreset.Square, new FrameRect(80, 0, 480, 480)) } },
        { "portrait.mp4", Segment with { Speed = 2, Loop = LoopMode.Pingpong } },
        { "portrait.mp4", Segment with { InFrame = 30, OutFrame = 150, Preset = SegmentPreset.Emoji } },
    };

    [Theory]
    [MemberData(nameof(Clips))]
    public async Task Renders_a_sticker_telegram_takes_with_what_it_promised(string clip, Segment segment)
    {
        var request = await RequestAsync(clip, segment, RenderEffort.Final);
        var limits = TelegramLimits.Current.For(segment.Preset);

        var sticker = await Engine.RenderAsync(request, progress: null, Cancel);
        var read = await ffmpeg.Probe.ProbeAsync(sticker.Path, Cancel);

        sticker.Validation!.Verdict.Should().Be(ValidationVerdict.Pass, string.Join("; ", sticker.Validation.Problems));
        sticker.Bytes.Should().Be(read.SizeBytes).And.BeLessThanOrEqualTo(limits.MaxBytes);
        read.Should().BeEquivalentTo(new { sticker.Output.Width, sticker.Output.Height, Fps = new Rational(sticker.Output.Fps, 1) });
        read.DurationSec.Should().BeApproximately(sticker.Output.DurationSec, 0.04);
        read.HasAlpha.Should().Be(request.Source.HasAlpha);
        sticker.Ladder[^1].Bytes.Should().Be(sticker.Bytes);
        sticker.Concessions.Should().Equal(sticker.Ladder[^1].Rung.Concessions);
    }

    [Theory]
    [MemberData(nameof(Clips))]
    public async Task Drafts_the_preview_at_the_size_and_rate_of_the_step_the_export_would_take(string clip, Segment segment)
    {
        var request = await RequestAsync(clip, segment, RenderEffort.Draft);

        var preview = await Engine.RenderAsync(request, progress: null, Cancel);
        var read = await ffmpeg.Probe.ProbeAsync(preview.Path, Cancel);

        preview.Validation.Should().BeNull();
        read.Should().BeEquivalentTo(new { preview.Output.Width, preview.Output.Height, Fps = new Rational(preview.Output.Fps, 1), SizeBytes = preview.Bytes });
    }

    [Fact]
    public async Task Renders_a_segment_without_fitting_as_it_is_even_over_the_limit()
    {
        var request = await RequestAsync("busy.mp4", Segment with { AutoFit = false }, RenderEffort.Final);

        var sticker = await Engine.RenderAsync(request, progress: null, Cancel);

        sticker.Should().BeEquivalentTo(new { Crf = 40, Concessions = Array.Empty<Concession>() });
        sticker.Ladder.Should().ContainSingle();
        sticker.Validation!.Checks.Should().ContainSingle(check => check.Rule == ValidationRule.Size)
            .Which.Status.Should().Be(sticker.Bytes <= TelegramLimits.Current.Sticker.MaxBytes ? CheckStatus.Pass : CheckStatus.Fail);
    }

    [Fact]
    public async Task Reports_the_passes_of_the_real_engine_rising_to_the_check()
    {
        var request = await RequestAsync("transparent.webm", Segment, RenderEffort.Final);
        var progress = new CollectProgress<RenderProgress>();

        await Engine.RenderAsync(request, progress, Cancel);

        var reports = progress.Reports;
        reports.Select(r => r.Fraction).Should().BeInAscendingOrder().And.EndWith(1.0);
        reports.Select(r => r.Stage).Distinct().Should().Equal(RenderStage.Probe, RenderStage.Pass1, RenderStage.Pass2, RenderStage.Validate);
    }

    [Fact]
    public async Task Stops_a_cancelled_render_in_its_second_pass_and_leaves_no_file()
    {
        var request = await RequestAsync("busy.mp4", Segment, RenderEffort.Final);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
        var progress = new CancelAt(RenderStage.Pass2, cancel);

        var render = () => Engine.RenderAsync(request, progress, cancel.Token);

        await render.Should().ThrowAsync<OperationCanceledException>();
        File.Exists(request.OutputPath).Should().BeFalse();
    }

    /// <summary>
    /// Cancels the render at the engine's first own report in <paramref name="stage"/>: the pass
    /// has started and FFmpeg is writing.
    /// </summary>
    private sealed class CancelAt(RenderStage stage, CancellationTokenSource cancel) : IProgress<RenderProgress>
    {
        private int _seen;

        public void Report(RenderProgress value)
        {
            if (value.Stage == stage && ++_seen == 2) cancel.Cancel();
        }
    }
}
