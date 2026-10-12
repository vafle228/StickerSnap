using StickerSnap.Application.Media;
using StickerSnap.Core.Media;
using StickerSnap.Core.Render;
using StickerSnap.Core.Telegram;
using StickerSnap.Infrastructure.Ffmpeg;
using StickerSnap.Tests.Core.Render;

namespace StickerSnap.Tests.Infrastructure.Ffmpeg;

public sealed class Vp9StickerArgumentsTests
{
    private static readonly StickerRender Render = new(
        "in/cat.mp4",
        Media.Landscape,
        StickerSpec.Default(Media.Landscape, TelegramLimits.Current.Sticker),
        new RateControl.ConstantQuality(31),
        RenderEffort.Final,
        "out/cat.webm");

    [Fact]
    public void Renders_a_crf_sticker_in_one_run()
    {
        Vp9StickerArguments.For(Render, 0, null).Should().Equal(
            "-t", "2.983333", "-i", "in/cat.mp4",
            "-filter_complex",
            "[0:v:0]trim=end_frame=90,setpts=PTS-STARTPTS,fps=30,tpad=stop=-1:stop_mode=clone,trim=end_frame=90," +
            "scale=1920:1080,setsar=1,crop=1080:1080:420:0:exact=1,scale=512:512:flags=lanczos,format=yuv420p[v]",
            "-map", "[v]", "-an", "-sn", "-dn",
            "-c:v", "libvpx-vp9", "-pix_fmt", "yuv420p",
            "-crf", "31", "-b:v", "0",
            "-deadline", "good", "-cpu-used", "2", "-row-mt", "1",
            "-f", "webm", "-y", "out/cat.webm");
    }

    [Fact]
    public void Analyses_without_a_file_in_pass_1_and_writes_it_in_pass_2()
    {
        var render = Render with { Rate = new RateControl.TwoPass(664_098) };

        var first = Vp9StickerArguments.For(render, 1, "tmp/vp9");
        var second = Vp9StickerArguments.For(render, 2, "tmp/vp9");

        first.Should().ContainInConsecutiveOrder("-b:v", "664098", "-pass", "1", "-passlogfile", "tmp/vp9");
        first.Should().EndWith(["-f", "null", "-"]);
        second.Should().ContainInConsecutiveOrder("-b:v", "664098", "-pass", "2", "-passlogfile", "tmp/vp9");
        second.Should().EndWith(["-f", "webm", "-y", "out/cat.webm"]);
    }

    [Fact]
    public void Refuses_a_two_pass_render_in_one_run()
    {
        var render = Render with { Rate = new RateControl.TwoPass(664_098) };

        var arguments = () => Vp9StickerArguments.For(render, 0, null);

        arguments.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Drafts_in_realtime_at_the_fastest_speed()
    {
        Vp9StickerArguments.For(Render with { Effort = RenderEffort.Draft }, 0, null)
            .Should().ContainInConsecutiveOrder("-deadline", "realtime", "-cpu-used", "8", "-row-mt", "1");
    }

    [Fact]
    public void Reads_the_alpha_of_vp9_with_libvpx_and_keeps_it()
    {
        var source = Media.Landscape with { Format = MediaFormat.Webm, Codec = "vp9", HasAlpha = true };
        var render = Render with { Source = source, Spec = StickerSpec.Default(source, TelegramLimits.Current.Sticker) };

        var arguments = Vp9StickerArguments.For(render, 0, null);

        arguments.Should().StartWith(["-c:v", "libvpx-vp9", "-t", "2.983333", "-i", "in/cat.mp4"]);
        arguments.Should().ContainInConsecutiveOrder("-c:v", "libvpx-vp9", "-pix_fmt", "yuva420p");
    }

    [Fact]
    public void Leaves_the_decoder_of_a_transparent_gif_to_ffmpeg()
    {
        var source = Media.Landscape with { Format = MediaFormat.Gif, Codec = "gif", HasAlpha = true };
        var render = Render with { Source = source, Spec = StickerSpec.Default(source, TelegramLimits.Current.Sticker) };

        Vp9StickerArguments.For(render, 0, null).Should().StartWith(["-i", "in/cat.mp4"]);
    }
}
