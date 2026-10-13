using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Render;
using StickerSnap.Infrastructure.Ffmpeg;

namespace StickerSnap.Tests.Infrastructure.Ffmpeg;

/// <summary>
/// What ffmpeg gets for the editor's pictures, written out in full, and how a failed run is told;
/// that the pictures come out as written, the slow tests show.
/// </summary>
public sealed class FfmpegEditorMediaTests : IDisposable
{
    private static readonly MediaInfo Ntsc = new(
        MediaFormat.Mp4, "h264", 1920, 1080, new Rational(30000, 1001), false, 300, 10.01, false, false, 3_000_000);

    private static readonly MediaInfo Gif = new(
        MediaFormat.Gif, "gif", 480, 270, new Rational(25, 2), true, 53, 4.24, true, false, 1_048_576);

    private static readonly MediaInfo TransparentWebm = new(
        MediaFormat.Webm, "vp9", 640, 480, new Rational(30, 1), false, 90, 3, true, false, 500_000);

    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("stickersnap-editor-media-");

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public void Dispose() => _dir.Delete(recursive: true);

    private static string Line(IReadOnlyList<string> arguments) => string.Join(' ', arguments);

    [Fact]
    public void Seeks_a_29_97_fps_video_to_the_strip_s_first_frame()
    {
        var strip = FrameStrip.Of(Ntsc, 60, 30, 96);

        var arguments = FfmpegEditorMedia.StripArguments("in.mp4", Ntsc, strip, "out.webp");

        // Frame 60 sits at 60 × 1001/30000 = 2.002 s: half a frame before it, and 30 frames on
        Line(arguments).Should().Be(
            "-ss 1.985317 -t 1.001 -i in.mp4 " +
            "-filter_complex [0:v:0]trim=end_frame=30,scale=171:96:flags=bicubic,setsar=1,tile=30x1[v] " +
            "-map [v] -frames:v 1 -c:v libwebp -quality 70 -f webp -y out.webp");
    }

    [Fact]
    public void Reads_a_gif_from_its_first_frame_and_trims_by_frame_numbers()
    {
        var strip = FrameStrip.Of(Gif, 30, 23, 48);

        var arguments = FfmpegEditorMedia.StripArguments("in.gif", Gif, strip, "out.webp");

        Line(arguments).Should().Be(
            "-i in.gif " +
            "-filter_complex [0:v:0]trim=start_frame=30:end_frame=53,scale=85:48:flags=bicubic,setsar=1,tile=23x1[v] " +
            "-map [v] -frames:v 1 -c:v libwebp -quality 70 -f webp -y out.webp");
    }

    [Fact]
    public void Reads_the_alpha_of_a_transparent_webm_with_libvpx()
    {
        var strip = FrameStrip.Of(TransparentWebm, 0, 30, 48);

        FfmpegEditorMedia.StripArguments("in.webm", TransparentWebm, strip, "out.webp").Take(2).Should().Equal("-c:v", "libvpx-vp9");
    }

    [Fact]
    public void Crops_and_scales_the_cover_s_frame()
    {
        var cover = new CoverShot(150, new PixelRect(420, 0, 1080, 1080), new PixelSize(384, 384));

        var arguments = FfmpegEditorMedia.CoverArguments("in.mp4", Ntsc, cover, "cover.webp");

        Line(arguments).Should().Be(
            "-ss 4.988317 -t 0.033367 -i in.mp4 " +
            "-filter_complex [0:v:0]trim=end_frame=1,scale=1920:1080,setsar=1,crop=1080:1080:420:0:exact=1,scale=384:384:flags=lanczos,setsar=1[v] " +
            "-map [v] -frames:v 1 -c:v libwebp -quality 85 -f webp -y cover.webp");
    }

    [Fact]
    public void Leaves_the_whole_picture_of_a_cover_uncropped()
    {
        var cover = new CoverShot(15, new PixelRect(0, 0, 480, 270), new PixelSize(384, 216));

        var arguments = FfmpegEditorMedia.CoverArguments("in.gif", Gif, cover, "cover.webp");

        Line(arguments).Should().Contain("[0:v:0]trim=start_frame=15:end_frame=16,scale=480:270,setsar=1,scale=384:216:flags=lanczos,setsar=1[v]");
    }

    [Fact]
    public void Retimes_every_frame_of_a_gif_to_n_over_fps_in_a_transparent_webm()
    {
        var arguments = FfmpegEditorMedia.PlayableArguments("in.gif", Gif, "copy.webm");

        Line(arguments).Should().Be(
            "-i in.gif " +
            "-filter_complex [0:v:0]setpts=N/(25/2)/TB,scale=480:270,setsar=1,format=yuva420p[v] " +
            "-map [v] -r 25/2 -fps_mode cfr -frames:v 53 -an " +
            "-c:v libvpx-vp9 -deadline realtime -cpu-used 8 -row-mt 1 -crf 32 -b:v 0 -f webm -y copy.webm");
    }

    [Fact]
    public void Writes_an_opaque_copy_without_alpha()
    {
        var mov = Ntsc with { Format = MediaFormat.Mov };

        Line(FfmpegEditorMedia.PlayableArguments("in.mov", mov, "copy.webm"))
            .Should().Contain("setpts=N/(30000/1001)/TB,scale=1920:1080,setsar=1,format=yuv420p[v]").And.Contain("-r 30000/1001");
    }

    [Fact]
    public async Task Tells_a_failed_run_as_render_failed_with_the_engine_s_words_and_leaves_no_file()
    {
        var output = Path.Combine(_dir.FullName, "out.webp");
        var ffmpeg = Substitute.For<IFfmpeg>();
        ffmpeg.RunAsync(Arg.Any<IReadOnlyList<string>>(), null, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            File.WriteAllText(output, "half");
            return new FfmpegResult(1, "", "Invalid data found when processing input");
        });

        var write = () => new FfmpegEditorMedia(ffmpeg).WriteStripAsync("in.gif", Gif, FrameStrip.Of(Gif, 0, 30, 48), output, Cancel);

        var error = (await write.Should().ThrowAsync<AppException>()).Which;
        error.Code.Should().Be(ErrorCode.RenderFailed);
        error.Log.Should().Contain("Invalid data");
        File.Exists(output).Should().BeFalse();
    }

    [Fact]
    public async Task Tells_a_full_disk_as_disk_full()
    {
        var ffmpeg = Substitute.For<IFfmpeg>();
        ffmpeg.RunAsync(Arg.Any<IReadOnlyList<string>>(), null, Arg.Any<CancellationToken>())
            .Returns(new FfmpegResult(1, "", "av_interleaved_write_frame(): No space left on device"));

        var write = () => new FfmpegEditorMedia(ffmpeg).WritePlayableAsync("in.gif", Gif, Path.Combine(_dir.FullName, "copy.webm"), Cancel);

        (await write.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.DiskFull);
    }
}
