using StickerSnap.Application.Media;
using StickerSnap.Core.Media;
using StickerSnap.Core.Render;
using StickerSnap.Core.Telegram;
using StickerSnap.Infrastructure.Ffmpeg;

namespace StickerSnap.Tests.Infrastructure.Ffmpeg;

/// <summary>
/// Stickers rendered by the real FFmpeg from clips it makes with lavfi, read back by the probe and
/// checked against Telegram's rules.
/// </summary>
[Trait("Category", Category.Slow)]
public sealed class RealStickerRendererTests(RealFfmpeg ffmpeg) : IClassFixture<RealFfmpeg>
{
    private static readonly PresetLimits Sticker = TelegramLimits.Current.Sticker;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private async Task<(RenderedSticker File, MediaInfo Read)> RenderAsync(string clip, Func<StickerSpec, RateControl> rate)
    {
        var source = await ffmpeg.Probe.ProbeAsync(clip, Cancel);
        var spec = StickerSpec.Default(source, Sticker);
        var render = new StickerRender(clip, source, spec, rate(spec), RenderEffort.Final, ffmpeg.Clip(Path.GetFileName(clip) + ".webm"));

        var file = await new FfmpegStickerRenderer(ffmpeg.Ffmpeg).RenderAsync(render, Cancel);
        return (file, await ffmpeg.Probe.ProbeAsync(file.Path, Cancel));
    }

    [Fact]
    public async Task Fits_the_first_3_seconds_of_a_landscape_video_into_256_kb_in_two_passes()
    {
        var clip = await ffmpeg.MakeAsync("landscape.mp4",
            "-f", "lavfi", "-i", "testsrc2=s=1280x720:r=30:d=5", "-c:v", "libopenh264", "-b:v", "4M");

        var (file, sticker) = await RenderAsync(clip, spec => new RateControl.TwoPass(TargetBitrate.BitsPerSecond(Sticker.MaxBytes, spec.DurationSec)));

        StickerCheck.Problems(sticker, Sticker).Should().BeEmpty();
        sticker.Should().BeEquivalentTo(new { Width = 512, Height = 512, FrameCount = 90, SizeBytes = file.SizeBytes });
        sticker.DurationSec.Should().BeApproximately(3, 0.05);
    }

    [Fact]
    public async Task Renders_a_portrait_video_at_another_frame_rate_at_a_constant_quality()
    {
        var clip = await ffmpeg.MakeAsync("portrait.mp4",
            "-f", "lavfi", "-i", "testsrc2=s=405x721:r=60:d=2", "-c:v", "libopenh264", "-pix_fmt", "yuv420p");

        var (_, sticker) = await RenderAsync(clip, _ => new RateControl.ConstantQuality(40));

        sticker.Should().BeEquivalentTo(new { Codec = "vp9", Width = 512, Height = 512, FrameCount = 60, HasAudio = false });
    }

    /// <summary>The alpha is a second stream at the same bitrate: a whole budget for each overshoots.</summary>
    [Fact]
    public async Task Fits_a_transparent_sticker_into_256_kb_in_two_passes_on_half_the_bitrate_each()
    {
        var clip = await ffmpeg.MakeAsync("transparent.webm",
            "-f", "lavfi", "-i", "testsrc2=s=640x480:r=30:d=3,format=yuva420p,geq=lum='lum(X,Y)':cb='cb(X,Y)':cr='cr(X,Y)':a='255*gt(hypot(X-320,Y-240),100+60*sin(T*4))'",
            "-c:v", "libvpx-vp9", "-pix_fmt", "yuva420p", "-b:v", "8M");

        var (_, sticker) = await RenderAsync(clip, spec => new RateControl.TwoPass(TargetBitrate.BitsPerSecond(Sticker.MaxBytes, spec.DurationSec, spec.KeepAlpha)));

        sticker.HasAlpha.Should().BeTrue();
        StickerCheck.Problems(sticker, Sticker).Should().BeEmpty();
    }

    [Fact]
    public async Task Keeps_the_alpha_of_a_transparent_vp9_source()
    {
        var clip = await ffmpeg.MakeAsync("alpha.webm",
            "-f", "lavfi", "-i", "testsrc2=s=320x240:r=30:d=1,format=yuva420p", "-c:v", "libvpx-vp9", "-pix_fmt", "yuva420p");

        var (_, sticker) = await RenderAsync(clip, _ => new RateControl.ConstantQuality(31));

        sticker.HasAlpha.Should().BeTrue();
        StickerCheck.Problems(sticker, Sticker).Should().BeEmpty();
    }
}
