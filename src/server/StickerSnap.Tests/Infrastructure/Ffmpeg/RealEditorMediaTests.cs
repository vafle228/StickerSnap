using System.Globalization;
using System.Text.RegularExpressions;
using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Render;
using StickerSnap.Infrastructure.Ffmpeg;
using StickerSnap.Tests.TestData;

namespace StickerSnap.Tests.Infrastructure.Ffmpeg;

/// <summary>
/// The editor's pictures made by the real FFmpeg. Clips are numbered: frame n is a grey of
/// brightness 4n, so a picture tells which frame it shows. Grey levels come back in video range,
/// a GIF's too: 16 + 4n × 219/255, a step of 3.4 between frames.
/// </summary>
[Trait("Category", Category.Slow)]
public sealed partial class RealEditorMediaTests(RealFfmpeg ffmpeg) : IClassFixture<RealFfmpeg>
{
    private readonly FfmpegEditorMedia _media = new(ffmpeg.Ffmpeg);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static double Grey(int frame) => 16 + frame * 4 * 219.0 / 255;

    /// <summary>A numbered clip of 64×36 at <paramref name="rate"/>.</summary>
    private Task<string> NumberedAsync(string name, string rate, double seconds, params string[] output) =>
        ffmpeg.MakeAsync(
            name,
            ["-f", "lavfi", "-i", $"color=c=black:s=64x36:r={rate}:d={seconds.ToString(CultureInfo.InvariantCulture)},format=gray,geq=lum='N*4'", .. output]);

    /// <summary>The mean brightness of the part of a picture at <paramref name="x"/>, or of the frame at <paramref name="seconds"/> of a video.</summary>
    private async Task<double> BrightnessAsync(string path, int x = 0, int width = 64, int height = 36, double? seconds = null)
    {
        List<string> arguments = [];
        if (seconds is { } at) arguments.AddRange(["-ss", at.ToString("0.######", CultureInfo.InvariantCulture)]);
        arguments.AddRange(["-i", path, "-frames:v", "1", "-vf", $"crop={width}:{height}:{x}:0,signalstats,metadata=print:key=lavfi.signalstats.YAVG:file=-", "-f", "null", "-"]);
        var result = await ffmpeg.Ffmpeg.RunAsync(arguments, progress: null, Cancel);
        result.IsSuccess.Should().BeTrue(result.Log);
        return double.Parse(Yavg().Match(result.Output).Groups[1].Value, CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"YAVG=([\d.]+)")]
    private static partial Regex Yavg();

    [Fact]
    public async Task Puts_the_frames_of_a_29_97_fps_video_side_by_side_from_the_one_asked_for()
    {
        var clip = await NumberedAsync("numbered.mp4", "30000/1001", 3, "-c:v", "libopenh264", "-b:v", "500k");
        var source = await ffmpeg.Probe.ProbeAsync(clip, Cancel);
        var strip = FrameStrip.Of(source, 20, 30, 36);
        var output = ffmpeg.Clip("strip.webp");

        await _media.WriteStripAsync(clip, source, strip, output, Cancel);

        var picture = await ffmpeg.Probe.ProbeAsync(output, Cancel);
        (picture.Width, picture.Height).Should().Be((64 * 30, 36));
        (await BrightnessAsync(output, x: 0)).Should().BeApproximately(Grey(20), 1.5);
        (await BrightnessAsync(output, x: 64 * 29)).Should().BeApproximately(Grey(49), 1.5);
    }

    [Fact]
    public async Task Takes_the_frames_of_a_gif_by_their_numbers()
    {
        var clip = await NumberedAsync("numbered.gif", "10", 4);
        var source = await ffmpeg.Probe.ProbeAsync(clip, Cancel);
        var strip = FrameStrip.Of(source, 30, 10, 36);
        var output = ffmpeg.Clip("gif-strip.webp");

        await _media.WriteStripAsync(clip, source, strip, output, Cancel);

        (await BrightnessAsync(output, x: 0)).Should().BeApproximately(Grey(30), 1.5);
        (await BrightnessAsync(output, x: 64 * 9)).Should().BeApproximately(Grey(39), 1.5);
    }

    [Fact]
    public async Task Makes_the_cover_of_the_frame_in_its_crop()
    {
        var clip = await NumberedAsync("cover.mp4", "25", 2, "-c:v", "libopenh264", "-b:v", "500k");
        var source = await ffmpeg.Probe.ProbeAsync(clip, Cancel);
        var cover = new CoverShot(30, new PixelRect(14, 0, 36, 36), new PixelSize(36, 36));
        var output = ffmpeg.Clip("cover.webp");

        await _media.WriteCoverAsync(clip, source, cover, output, Cancel);

        var picture = await ffmpeg.Probe.ProbeAsync(output, Cancel);
        (picture.Width, picture.Height).Should().Be((36, 36));
        (await BrightnessAsync(output, width: 36)).Should().BeApproximately(Grey(30), 1.5);
    }

    [Fact]
    public async Task Copies_a_gif_frame_for_frame_with_frame_n_at_n_over_fps()
    {
        // 7 frames a second do not land on a GIF's hundredths: the copy must still keep every one
        var clip = await NumberedAsync("rate.gif", "7", 6);
        var source = await ffmpeg.Probe.ProbeAsync(clip, Cancel);
        var output = ffmpeg.Clip("rate.webm");

        await _media.WritePlayableAsync(clip, source, output, Cancel);

        var copy = await ffmpeg.Probe.ProbeAsync(output, Cancel);
        copy.Format.Should().Be(MediaFormat.Webm);
        copy.FrameCount.Should().Be(source.FrameCount);
        copy.IsVariableFrameRate.Should().BeFalse();
        BrowserPlayback.PlaysAsIs(copy).Should().BeTrue();
        var rate = source.Fps.ToDouble();
        (await BrightnessAsync(output, seconds: 25 / rate)).Should().BeApproximately(Grey(25), 2.5);
    }

    [Fact]
    public async Task Keeps_the_transparency_of_a_transparent_gif_in_the_copy()
    {
        var gif = await TestFiles.CopyToAsync("Corpus/21-sticker-cat.gif", Path.GetDirectoryName(ffmpeg.Clip("x"))!, Cancel);
        var source = await ffmpeg.Probe.ProbeAsync(gif, Cancel);
        source.HasAlpha.Should().BeTrue("the corpus sticker is transparent");
        var output = ffmpeg.Clip("sticker-cat.webm");

        await _media.WritePlayableAsync(gif, source, output, Cancel);

        var copy = await ffmpeg.Probe.ProbeAsync(output, Cancel);
        copy.HasAlpha.Should().BeTrue();
        copy.FrameCount.Should().Be(source.FrameCount);
        copy.Fps.Should().Be(source.Fps);
    }
}
