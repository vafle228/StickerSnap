using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;
using StickerSnap.Infrastructure.Ffmpeg;

namespace StickerSnap.Tests.Infrastructure.Ffmpeg;

/// <summary>
/// The adapters over the real FFmpeg, on 1-second clips that FFmpeg makes itself with lavfi.
/// The encoders are those of the pinned LGPL build: libopenh264, libvpx-vp9, libwebp_anim, gif.
/// </summary>
[Trait("Category", Category.Slow)]
public sealed class RealFfmpegTests(RealFfmpeg ffmpeg) : IClassFixture<RealFfmpeg>
{
    private const string Picture = "testsrc2=s=320x240:r=30:d=1";

    /// <summary>A red square on a transparent field: GIF and WEBP with a pixel that stays clear.</summary>
    private const string Transparent =
        "color=c=black@0:s=160x120:r=10:d=1,format=rgba,drawbox=x=40:y=30:w=80:h=60:c=red:t=fill";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Probes_an_h264_mp4_with_sound()
    {
        var clip = await ffmpeg.MakeAsync("h264.mp4",
            "-f", "lavfi", "-i", Picture, "-f", "lavfi", "-i", "sine=d=1",
            "-c:v", "libopenh264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest");

        var media = await ffmpeg.Probe.ProbeAsync(clip, Cancel);

        media.Should().BeEquivalentTo(new
        {
            Format = MediaFormat.Mp4, Codec = "h264", Width = 320, Height = 240, Fps = new Rational(30, 1),
            IsVariableFrameRate = false, FrameCount = 30, HasAlpha = false, HasAudio = true,
            SizeBytes = new FileInfo(clip).Length,
        });
        media.DurationSec.Should().BeApproximately(1, 0.05);
    }

    [Fact]
    public async Task Probes_the_size_of_a_rotated_video_as_it_is_shown()
    {
        var upright = await ffmpeg.MakeAsync("upright.mp4", "-f", "lavfi", "-i", Picture, "-c:v", "libopenh264");
        var rotated = await ffmpeg.MakeAsync("rotated.mp4", "-display_rotation", "90", "-i", upright, "-c", "copy");

        var media = await ffmpeg.Probe.ProbeAsync(rotated, Cancel);

        (media.Width, media.Height).Should().Be((240, 320));
    }

    [Fact]
    public async Task Finds_the_alpha_of_vp9_that_pix_fmt_hides()
    {
        var withAlpha = await ffmpeg.MakeAsync("alpha.webm",
            "-f", "lavfi", "-i", $"{Picture},format=yuva420p", "-c:v", "libvpx-vp9", "-pix_fmt", "yuva420p");
        var opaque = await ffmpeg.MakeAsync("opaque.webm", "-f", "lavfi", "-i", Picture, "-c:v", "libvpx-vp9");

        (await ffmpeg.Probe.ProbeAsync(withAlpha, Cancel)).HasAlpha.Should().BeTrue();
        (await ffmpeg.Probe.ProbeAsync(opaque, Cancel)).Should().BeEquivalentTo(new
        {
            Format = MediaFormat.Webm, Codec = "vp9", FrameCount = 30, HasAlpha = false,
        });
    }

    [Fact]
    public async Task Tells_a_transparent_gif_from_an_opaque_one_by_its_pixels()
    {
        var transparent = await ffmpeg.MakeAsync("transparent.gif", "-f", "lavfi", "-i",
            $"{Transparent},split[a][b];[a]palettegen=reserve_transparent=1[p];[b][p]paletteuse=alpha_threshold=128");
        var opaque = await ffmpeg.MakeAsync("opaque.gif", "-f", "lavfi", "-i", "testsrc2=s=160x120:r=10:d=1");

        (await ffmpeg.Probe.ProbeAsync(transparent, Cancel)).HasAlpha.Should().BeTrue();
        (await ffmpeg.Probe.ProbeAsync(opaque, Cancel)).Should().BeEquivalentTo(new
        {
            Format = MediaFormat.Gif, Codec = "gif", Fps = new Rational(10, 1), FrameCount = 10, HasAlpha = false,
        });
    }

    [Fact]
    public async Task Tells_a_transparent_animated_webp_from_an_opaque_one_by_its_pixels()
    {
        var transparent = await ffmpeg.MakeAsync("transparent.webp", "-f", "lavfi", "-i",
            "testsrc2=s=160x120:r=10:d=1,format=rgba,geq=r='r(X,Y)':g='g(X,Y)':b='b(X,Y)':a='if(lt(X,40),0,255)'",
            "-c:v", "libwebp_anim", "-loop", "0");
        var opaque = await ffmpeg.MakeAsync("opaque.webp",
            "-f", "lavfi", "-i", "testsrc2=s=160x120:r=10:d=1", "-c:v", "libwebp_anim", "-loop", "0");

        (await ffmpeg.Probe.ProbeAsync(transparent, Cancel)).HasAlpha.Should().BeTrue();
        (await ffmpeg.Probe.ProbeAsync(opaque, Cancel)).Should().BeEquivalentTo(new
        {
            Format = MediaFormat.Webp, Codec = "webp", FrameCount = 10, DurationSec = 1.0, HasAlpha = false,
        });
    }

    [Fact]
    public async Task Answers_a_broken_file_with_the_words_of_ffprobe()
    {
        var whole = await ffmpeg.MakeAsync("whole.mp4", "-f", "lavfi", "-i", Picture, "-c:v", "libopenh264");
        var broken = ffmpeg.Clip("broken.mp4");
        await File.WriteAllBytesAsync(broken, (await File.ReadAllBytesAsync(whole, Cancel))[..3000], Cancel);

        var probe = () => ffmpeg.Probe.ProbeAsync(broken, Cancel);

        var error = (await probe.Should().ThrowAsync<AppException>()).Which;
        error.Code.Should().Be(ErrorCode.SourceUnreadable);
        error.Log.Should().Contain("moov atom not found");
    }

    [Fact]
    public async Task Reports_progress_until_the_end()
    {
        var reports = new List<FfmpegProgress>();

        var result = await ffmpeg.Ffmpeg.RunAsync(
            ["-f", "lavfi", "-i", Picture, "-f", "null", "-"], new Recorder(reports.Add), Cancel);

        result.IsSuccess.Should().BeTrue(result.Log);
        reports.Should().NotBeEmpty();
        reports[^1].Should().BeEquivalentTo(new { Frame = 30L, IsEnd = true });
    }

    [Fact]
    public async Task Returns_a_failed_run_with_the_end_of_its_log()
    {
        var result = await ffmpeg.Ffmpeg.RunAsync(["-i", ffmpeg.Clip("nothing-here.mp4"), "-f", "null", "-"], null, Cancel);

        result.IsSuccess.Should().BeFalse();
        result.Log.Should().Contain("nothing-here.mp4");
    }

    [Fact]
    public async Task Kills_ffmpeg_when_the_run_is_cancelled()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
        // An endless source read in real time: the run ends only by cancelling it
        var run = ffmpeg.Ffmpeg.RunAsync(
            ["-re", "-f", "lavfi", "-i", "testsrc2=s=320x240:r=30", "-f", "null", "-"],
            new Recorder(_ => cancel.Cancel()),
            cancel.Token);

        await run.Awaiting(r => r).Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>Reports at once on the reading thread, unlike Progress&lt;T&gt;.</summary>
    private sealed class Recorder(Action<FfmpegProgress> report) : IProgress<FfmpegProgress>
    {
        public void Report(FfmpegProgress value) => report(value);
    }
}
