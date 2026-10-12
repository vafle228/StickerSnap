using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;
using StickerSnap.Infrastructure.Ffmpeg;

namespace StickerSnap.Tests.Infrastructure.Ffmpeg;

/// <summary>
/// Samples/ holds what the pinned ffprobe 9.0.2 printed for 1-second clips made with lavfi
/// (testsrc2, libopenh264, libvpx-vp9, libwebp_anim) and for animated WEBP files with uneven
/// delays made with libwebp's own encoder (the webp-anim-*-delays files); no FFmpeg runs here.
/// </summary>
public sealed class FfprobeOutputTests
{
    private static MediaInfo Parse(string sample, string extension = "mp4") => FfprobeOutput.Parse(Sample(sample), extension);

    private static string Sample(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Infrastructure", "Ffmpeg", "Samples", name));

    [Fact]
    public void Reads_an_h264_mp4_with_sound()
    {
        Parse("mp4-h264-audio.json").Should().Be(new MediaInfo(
            MediaFormat.Mp4, "h264", 320, 240, new Rational(30, 1), IsVariableFrameRate: false,
            FrameCount: 30, DurationSec: 1, HasAlpha: false, HasAudio: true, SizeBytes: 152_437));
    }

    [Fact]
    public void Tells_quicktime_from_mp4_by_the_brand_not_the_name()
    {
        Parse("mov-h264.json", "mp4").Format.Should().Be(MediaFormat.Mov);
    }

    [Fact]
    public void Reads_a_gif()
    {
        var gif = Parse("gif.json", "gif");

        gif.Format.Should().Be(MediaFormat.Gif);
        gif.Codec.Should().Be("gif");
        gif.Fps.Should().Be(new Rational(10, 1));
        gif.FrameCount.Should().Be(10);
        // bgra, like every GIF: the probe decodes the frames to tell whether a pixel is transparent
        gif.HasAlpha.Should().BeTrue();
    }

    [Fact]
    public void Takes_the_alpha_of_vp9_from_its_tag_while_pix_fmt_says_yuv420p()
    {
        Parse("webm-vp9-alpha.json", "webm").HasAlpha.Should().BeTrue();
        Parse("webm-vp9-opaque.json", "webm").HasAlpha.Should().BeFalse();
    }

    [Fact]
    public void Counts_the_frames_of_a_webm_that_does_not_store_their_number()
    {
        var webm = Parse("webm-vp9-opaque.json", "webm");

        webm.Format.Should().Be(MediaFormat.Webm);
        webm.FrameCount.Should().Be(30);
        webm.DurationSec.Should().Be(1);
    }

    [Fact]
    public void Reads_an_animated_webp_that_has_no_duration_in_the_container()
    {
        var webp = Parse("webp-anim-alpha.json", "webp");

        webp.Format.Should().Be(MediaFormat.Webp);
        webp.Codec.Should().Be("webp");
        webp.FrameCount.Should().Be(10);
        webp.DurationSec.Should().Be(1);
        webp.HasAlpha.Should().BeTrue();
    }

    [Fact]
    public void Takes_the_length_of_an_animated_webp_from_its_frame_delays()
    {
        // Delays 40, 40, 200, 40, 500, 100, 20, 20, 20, 1000, 60, 60 ms: ffprobe's average rate 59/12 would make it 2,44 s
        var webp = FfprobeOutput.WithFrameDelays(
            Parse("webp-anim-variable-delays.json", "webp"), Sample("webp-anim-variable-delays.packets.json"));

        webp.FrameCount.Should().Be(12);
        webp.DurationSec.Should().BeApproximately(2.1, 1e-9);
        webp.IsVariableFrameRate.Should().BeTrue();
        webp.Fps.Should().Be(new Rational(40, 7));
    }

    [Fact]
    public void Keeps_the_rate_of_a_webp_whose_delays_round_to_whole_milliseconds()
    {
        // 60 frames at 30 fps stored as 33, 33, 34 ms
        var webp = new MediaInfo(MediaFormat.Webp, "webp", 320, 240, new Rational(30, 1), IsVariableFrameRate: false,
            FrameCount: 60, DurationSec: 2, HasAlpha: false, HasAudio: false, SizeBytes: 262_998);

        FfprobeOutput.WithFrameDelays(webp, Sample("webp-anim-30fps.packets.json")).Should().Be(webp with { DurationSec = 1.999 });
    }

    [Fact]
    public void Plays_webp_delays_of_ten_milliseconds_and_less_as_a_tenth_of_a_second_like_browsers()
    {
        // The file says 0, 0, 10, 0, 50, 100 ms; FFmpeg's demuxer reads 100, 100, 100, 100, 50, 100
        var webp = FfprobeOutput.WithFrameDelays(
            Parse("webp-anim-short-delays.json", "webp"), Sample("webp-anim-short-delays.packets.json"));

        webp.FrameCount.Should().Be(6);
        webp.DurationSec.Should().BeApproximately(0.55, 1e-9);
        webp.IsVariableFrameRate.Should().BeTrue();
    }

    [Fact]
    public void Leaves_the_media_as_it_was_when_ffprobe_lists_no_frames()
    {
        var webp = Parse("webp-anim-alpha.json", "webp");

        FfprobeOutput.WithFrameDelays(webp, """{ "packets": [], "streams": [{ "time_base": "1/1000" }] }""").Should().Be(webp);
    }

    [Fact]
    public void Tells_a_variable_frame_rate_by_the_average_rate()
    {
        var vfr = Parse("mp4-vfr.json");

        vfr.IsVariableFrameRate.Should().BeTrue();
        vfr.Fps.Should().Be(new Rational(900, 59));
        vfr.FrameCount.Should().Be(30);
        vfr.DurationSec.Should().BeApproximately(1.967, 0.001);
    }

    [Fact]
    public void Swaps_the_size_of_a_video_turned_a_quarter()
    {
        var rotated = Parse("mp4-rotated.json");

        (rotated.Width, rotated.Height).Should().Be((240, 320));
    }

    [Fact]
    public void Stretches_the_width_by_a_non_square_pixel_aspect_ratio()
    {
        var stretched = Parse("mp4-sar.json");

        (stretched.Width, stretched.Height).Should().Be((640, 240));
    }

    [Fact]
    public void Finds_a_file_with_sound_only_unreadable()
    {
        var parse = () => Parse("m4a-audio-only.json");

        parse.Should().Throw<AppException>()
            .Where(e => e.Code == ErrorCode.SourceUnreadable && e.Log == "The file has no video stream");
    }

    [Fact]
    public void Finds_broken_output_unreadable()
    {
        var parse = () => Parse("truncated.json");

        parse.Should().Throw<AppException>().Where(e => e.Code == ErrorCode.SourceUnreadable && e.Log!.StartsWith("ffprobe printed no JSON"));
    }

    [Fact]
    public void Refuses_a_matroska_file_named_webm_whose_codec_webm_does_not_allow()
    {
        var parse = () => Parse("webm-matroska-h264.json", "webm");

        parse.Should().Throw<AppException>()
            .Where(e => e.Code == ErrorCode.FormatUnsupported && (string)e.Params["ext"] == "webm");
    }

    [Fact]
    public void Leaves_out_cover_art()
    {
        const string json = """
            {
              "streams": [
                { "codec_type": "video", "codec_name": "mjpeg", "width": 600, "height": 600,
                  "r_frame_rate": "90000/1", "avg_frame_rate": "0/0", "disposition": { "attached_pic": 1 } },
                { "codec_type": "video", "codec_name": "h264", "width": 320, "height": 240,
                  "r_frame_rate": "25/1", "avg_frame_rate": "25/1", "nb_frames": "50", "disposition": { "attached_pic": 0 } }
              ],
              "format": { "format_name": "mov,mp4,m4a,3gp,3g2,mj2", "duration": "2.000000", "size": "1000",
                          "tags": { "major_brand": "isom" } }
            }
            """;

        var media = FfprobeOutput.Parse(json, "mp4");

        media.Codec.Should().Be("h264");
        media.FrameCount.Should().Be(50);
    }
}
