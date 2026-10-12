using StickerSnap.Application.Media;
using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Render;
using StickerSnap.Infrastructure.Ffmpeg;

namespace StickerSnap.Tests.Infrastructure.Ffmpeg;

/// <summary>
/// Golden scenes: a segment of a source, compiled by <see cref="SceneCompiler"/>, and the input
/// options and filtergraph ffmpeg gets for it, written out in full. A change of the graph shows
/// here as a diff to read, scene by scene; that it plays as written, the slow tests show.
/// </summary>
public sealed class StickerFilterGraphTests
{
    private static readonly MediaInfo Video = new(
        MediaFormat.Mp4, "h264", 1920, 1080, new Rational(30, 1), false, 150, 5, false, false, 3_000_000);

    private static readonly MediaInfo Ntsc = Video with { Fps = new Rational(30000, 1001), FrameCount = 300, DurationSec = 10.01 };

    private static readonly MediaInfo OddPortrait = Video with { Width = 405, Height = 721, Fps = new Rational(60, 1), FrameCount = 240, DurationSec = 4 };

    private static readonly MediaInfo PhoneVfr = Video with { Width = 1080, Height = 1920, IsVariableFrameRate = true, FrameCount = 200, DurationSec = 6.67 };

    private static readonly MediaInfo Gif = new(
        MediaFormat.Gif, "gif", 480, 270, new Rational(10, 1), false, 31, 3.1, false, false, 1_048_576);

    private static readonly MediaInfo TransparentGif = Gif with { Fps = new Rational(15, 1), FrameCount = 60, DurationSec = 4, HasAlpha = true };

    private static readonly MediaInfo TransparentWebm = new(
        MediaFormat.Webm, "vp9", 640, 480, new Rational(30, 1), false, 90, 3, true, false, 500_000);

    private static readonly MediaInfo Webp = new(
        MediaFormat.Webp, "webp", 400, 400, new Rational(0, 0), true, 20, 2.1, true, false, 200_000);

    private static readonly Segment Square = new(
        "seg-1", "Стикер 1", 0, 89, [], new SegmentFrame.Crop(CropPreset.Square, new FrameRect(420, 0, 1080, 1080)),
        Speed: 1, LoopMode.Normal, SegmentPreset.Sticker, LongSticker: false, AutoFit: true);

    private static Segment Crop(double x, double y, double width, double height, CropPreset preset = CropPreset.Free) =>
        Square with { Frame = new SegmentFrame.Crop(preset, new FrameRect(x, y, width, height)) };

    private static Segment Stretch(StretchPreset preset, double width, double height) =>
        Square with { Frame = new SegmentFrame.Stretch(preset, new FrameAspect(width, height)) };

    /// <summary>
    /// The scene's name, the source, the segment, the step of the ladder (0 for the first, -1 for the
    /// last), and what ffmpeg gets: the input options and the filtergraph.
    /// </summary>
    public static TheoryData<string, MediaInfo, Segment, int, string, string> Scenes => new()
    {
        {
            "square crop of a video", Video, Square, 0,
            "-t 2.983333 -i in.src",
            "[0:v:0]trim=end_frame=90,setpts=PTS-STARTPTS,fps=30,tpad=stop=-1:stop_mode=clone,trim=end_frame=90,scale=1920:1080,setsar=1,crop=1080:1080:420:0:exact=1,scale=512:512:flags=lanczos,setsar=1,format=yuv420p[v]"
        },
        {
            "frames from the middle of a video", Video, Square with { InFrame = 40, OutFrame = 99 }, 0,
            "-ss 1.316667 -t 2 -i in.src",
            "[0:v:0]trim=end_frame=60,setpts=PTS-STARTPTS,fps=30,tpad=stop=-1:stop_mode=clone,trim=end_frame=60,scale=1920:1080,setsar=1,crop=1080:1080:420:0:exact=1,scale=512:512:flags=lanczos,setsar=1,format=yuv420p[v]"
        },
        {
            "the whole landscape picture", Video, Crop(0, 0, 1920, 1080, CropPreset.Source), 0,
            "-t 2.983333 -i in.src",
            "[0:v:0]trim=end_frame=90,setpts=PTS-STARTPTS,fps=30,tpad=stop=-1:stop_mode=clone,trim=end_frame=90,scale=1920:1080,setsar=1,scale=512:288:flags=lanczos,setsar=1,format=yuv420p[v]"
        },
        {
            "an odd portrait picture", OddPortrait, Crop(0, 0, 405, 721, CropPreset.Source), 0,
            "-t 1.491667 -i in.src",
            "[0:v:0]trim=end_frame=90,setpts=PTS-STARTPTS,fps=30,tpad=stop=-1:stop_mode=clone,trim=end_frame=45,scale=405:721,setsar=1,scale=288:512:flags=lanczos,setsar=1,format=yuv420p[v]"
        },
        {
            "a crop at odd pixels", Video, Crop(3, 5, 241, 181), 0,
            "-t 2.983333 -i in.src",
            "[0:v:0]trim=end_frame=90,setpts=PTS-STARTPTS,fps=30,tpad=stop=-1:stop_mode=clone,trim=end_frame=90,scale=1920:1080,setsar=1,crop=241:181:3:5:exact=1,scale=512:384:flags=lanczos,setsar=1,format=yuv420p[v]"
        },
        {
            "a stretch to the source's proportion", Video, Stretch(StretchPreset.Source, 16, 9), 0,
            "-t 2.983333 -i in.src",
            "[0:v:0]trim=end_frame=90,setpts=PTS-STARTPTS,fps=30,tpad=stop=-1:stop_mode=clone,trim=end_frame=90,scale=1920:1080,setsar=1,scale=512:288:flags=lanczos,setsar=1,format=yuv420p[v]"
        },
        {
            "a stretch squashed to a square", Video, Stretch(StretchPreset.Squash, 1, 1), 0,
            "-t 2.983333 -i in.src",
            "[0:v:0]trim=end_frame=90,setpts=PTS-STARTPTS,fps=30,tpad=stop=-1:stop_mode=clone,trim=end_frame=90,scale=1920:1080,setsar=1,scale=512:512:flags=lanczos,setsar=1,format=yuv420p[v]"
        },
        {
            "a free stretch to 21:9", Video, Stretch(StretchPreset.Free, 21, 9), 0,
            "-t 2.983333 -i in.src",
            "[0:v:0]trim=end_frame=90,setpts=PTS-STARTPTS,fps=30,tpad=stop=-1:stop_mode=clone,trim=end_frame=90,scale=1920:1080,setsar=1,scale=512:220:flags=lanczos,setsar=1,format=yuv420p[v]"
        },
        {
            "a quarter speed", Video, Square with { OutFrame = 29, Speed = 0.25 }, 0,
            "-t 0.75 -i in.src",
            "[0:v:0]trim=end_frame=23,setpts=(PTS-STARTPTS)/0.25,fps=30,tpad=stop=-1:stop_mode=clone,trim=end_frame=90,scale=1920:1080,setsar=1,crop=1080:1080:420:0:exact=1,scale=512:512:flags=lanczos,setsar=1,format=yuv420p[v]"
        },
        {
            "half speed", Video, Square with { Speed = 0.5 }, 0,
            "-t 1.483333 -i in.src",
            "[0:v:0]trim=end_frame=45,setpts=(PTS-STARTPTS)/0.5,fps=15,tpad=stop=-1:stop_mode=clone,trim=end_frame=45,scale=1920:1080,setsar=1,crop=1080:1080:420:0:exact=1,scale=512:512:flags=lanczos,setsar=1,format=yuv420p[v]"
        },
        {
            "one and a half speed", Video, Square with { OutFrame = 149, Speed = 1.5 }, 0,
            "-t 4.483333 -i in.src",
            "[0:v:0]trim=end_frame=135,setpts=(PTS-STARTPTS)/1.5,fps=30,tpad=stop=-1:stop_mode=clone,trim=end_frame=90,scale=1920:1080,setsar=1,crop=1080:1080:420:0:exact=1,scale=512:512:flags=lanczos,setsar=1,format=yuv420p[v]"
        },
        {
            "four times as fast", Video, Square with { OutFrame = 149, Speed = 4 }, 0,
            "-t 4.983333 -i in.src",
            "[0:v:0]trim=end_frame=150,setpts=(PTS-STARTPTS)/4,fps=30,tpad=stop=-1:stop_mode=clone,trim=end_frame=38,scale=1920:1080,setsar=1,crop=1080:1080:420:0:exact=1,scale=512:512:flags=lanczos,setsar=1,format=yuv420p[v]"
        },
        {
            "a ping-pong", Video, Square with { Loop = LoopMode.Pingpong }, 0,
            "-t 1.516667 -i in.src",
            "[0:v:0]trim=end_frame=46,setpts=PTS-STARTPTS,fps=30,tpad=stop=-1:stop_mode=clone,trim=end_frame=46,scale=1920:1080,setsar=1,crop=1080:1080:420:0:exact=1,scale=512:512:flags=lanczos,setsar=1,split[forth][back];"
            + "[back]reverse,trim=start_frame=1:end_frame=45,setpts=PTS-STARTPTS[backward];"
            + "[forth][backward]concat=n=2:v=1:a=0,format=yuv420p[v]"
        },
        {
            "a ping-pong twice as fast", Video, Square with { InFrame = 10, OutFrame = 149, Speed = 2, Loop = LoopMode.Pingpong }, 0,
            "-ss 0.316667 -t 3.066667 -i in.src",
            "[0:v:0]trim=end_frame=92,setpts=(PTS-STARTPTS)/2,fps=30,tpad=stop=-1:stop_mode=clone,trim=end_frame=46,scale=1920:1080,setsar=1,crop=1080:1080:420:0:exact=1,scale=512:512:flags=lanczos,setsar=1,split[forth][back];"
            + "[back]reverse,trim=start_frame=1:end_frame=45,setpts=PTS-STARTPTS[backward];"
            + "[forth][backward]concat=n=2:v=1:a=0,format=yuv420p[v]"
        },
        {
            "a ping-pong of two frames", Video, Square with { InFrame = 5, OutFrame = 6, Loop = LoopMode.Pingpong }, 0,
            "-ss 0.15 -t 0.066667 -i in.src",
            "[0:v:0]trim=end_frame=2,setpts=PTS-STARTPTS,fps=30,tpad=stop=-1:stop_mode=clone,trim=end_frame=2,scale=1920:1080,setsar=1,crop=1080:1080:420:0:exact=1,scale=512:512:flags=lanczos,setsar=1,format=yuv420p[v]"
        },
        {
            "a 29.97 fps video sought", Ntsc, Square with { InFrame = 120, OutFrame = 299 }, 0,
            "-ss 3.987317 -t 3.003 -i in.src",
            "[0:v:0]trim=end_frame=90,setpts=PTS-STARTPTS,fps=30,tpad=stop=-1:stop_mode=clone,trim=end_frame=90,scale=1920:1080,setsar=1,crop=1080:1080:420:0:exact=1,scale=512:512:flags=lanczos,setsar=1,format=yuv420p[v]"
        },
        {
            "a 60 fps odd portrait video", OddPortrait, Crop(0, 158, 404, 404, CropPreset.Square) with { InFrame = 60, OutFrame = 239 }, 0,
            "-ss 0.991667 -t 3 -i in.src",
            "[0:v:0]trim=end_frame=180,setpts=PTS-STARTPTS,fps=30,tpad=stop=-1:stop_mode=clone,trim=end_frame=90,scale=405:721,setsar=1,crop=404:404:0:158:exact=1,scale=512:512:flags=lanczos,setsar=1,format=yuv420p[v]"
        },
        {
            "a 10 fps GIF at its own rate", Gif, Crop(105, 0, 270, 270, CropPreset.Square) with { OutFrame = 30 }, 0,
            "-i in.src",
            "[0:v:0]trim=end_frame=30,setpts=PTS-STARTPTS,fps=10,tpad=stop=-1:stop_mode=clone,trim=end_frame=30,scale=480:270,setsar=1,crop=270:270:105:0:exact=1,scale=512:512:flags=lanczos,setsar=1,format=yuv420p[v]"
        },
        {
            "a GIF from its middle", Gif, Crop(105, 0, 270, 270, CropPreset.Square) with { InFrame = 10, OutFrame = 20 }, 0,
            "-i in.src",
            "[0:v:0]trim=start_frame=10:end_frame=21,setpts=PTS-STARTPTS,fps=10,tpad=stop=-1:stop_mode=clone,trim=end_frame=11,scale=480:270,setsar=1,crop=270:270:105:0:exact=1,scale=512:512:flags=lanczos,setsar=1,format=yuv420p[v]"
        },
        {
            "a variable rate phone video from its middle", PhoneVfr, Crop(0, 420, 1080, 1080, CropPreset.Square) with { InFrame = 100, OutFrame = 199 }, 0,
            "-i in.src",
            "[0:v:0]trim=start_frame=100:end_frame=190,setpts=PTS-STARTPTS,fps=30,tpad=stop=-1:stop_mode=clone,trim=end_frame=90,scale=1080:1920,setsar=1,crop=1080:1080:0:420:exact=1,scale=512:512:flags=lanczos,setsar=1,format=yuv420p[v]"
        },
        {
            "a custom emoji", Video, Square with { Preset = SegmentPreset.Emoji }, 0,
            "-t 2.983333 -i in.src",
            "[0:v:0]trim=end_frame=90,setpts=PTS-STARTPTS,fps=30,tpad=stop=-1:stop_mode=clone,trim=end_frame=90,scale=1920:1080,setsar=1,crop=1080:1080:420:0:exact=1,scale=100:100:flags=lanczos,setsar=1,format=yuv420p[v]"
        },
        {
            "a stretched ping-pong emoji", Gif, Stretch(StretchPreset.Squash, 1, 1) with { InFrame = 10, OutFrame = 20, Speed = 1.5, Loop = LoopMode.Pingpong, Preset = SegmentPreset.Emoji }, 0,
            "-i in.src",
            "[0:v:0]trim=start_frame=10:end_frame=21,setpts=(PTS-STARTPTS)/1.5,fps=15,tpad=stop=-1:stop_mode=clone,trim=end_frame=11,scale=480:270,setsar=1,scale=100:100:flags=lanczos,setsar=1,split[forth][back];"
            + "[back]reverse,trim=start_frame=1:end_frame=10,setpts=PTS-STARTPTS[backward];"
            + "[forth][backward]concat=n=2:v=1:a=0,format=yuv420p[v]"
        },
        {
            "a transparent VP9 video", TransparentWebm, Crop(80, 0, 480, 480, CropPreset.Square) with { InFrame = 30 }, 0,
            "-c:v libvpx-vp9 -ss 0.983333 -t 2 -i in.src",
            "[0:v:0]trim=end_frame=60,setpts=PTS-STARTPTS,fps=30,tpad=stop=-1:stop_mode=clone,trim=end_frame=60,scale=640:480,setsar=1,crop=480:480:80:0:exact=1,scale=512:512:flags=lanczos,setsar=1,format=yuva420p[v]"
        },
        {
            "a transparent GIF", TransparentGif, Crop(105, 0, 270, 270, CropPreset.Square) with { OutFrame = 59 }, 0,
            "-i in.src",
            "[0:v:0]trim=end_frame=45,setpts=PTS-STARTPTS,fps=15,tpad=stop=-1:stop_mode=clone,trim=end_frame=45,scale=480:270,setsar=1,crop=270:270:105:0:exact=1,scale=512:512:flags=lanczos,setsar=1,format=yuva420p[v]"
        },
        {
            "an animated WEBP", Webp, Crop(0, 0, 400, 400, CropPreset.Source) with { OutFrame = 19 }, 0,
            "-i in.src",
            "[0:v:0]trim=end_frame=20,setpts=PTS-STARTPTS,fps=30,tpad=stop=-1:stop_mode=clone,trim=end_frame=63,scale=400:400,setsar=1,scale=512:512:flags=lanczos,setsar=1,format=yuva420p[v]"
        },
        {
            "excluded frames", Video, Square with { OutFrame = 19, ExcludedFrames = [3, 7, 8, 9, 19] }, 0,
            "-t 0.65 -i in.src",
            "[0:v:0]trim=end_frame=20,select='between(n,0,2)+between(n,4,6)+between(n,10,18)',setpts=N/(30)/TB,fps=30,tpad=stop=-1:stop_mode=clone,trim=end_frame=15,scale=1920:1080,setsar=1,crop=1080:1080:420:0:exact=1,scale=512:512:flags=lanczos,setsar=1,format=yuv420p[v]"
        },
        {
            "excluded frames of a sought video at half speed", Ntsc, Square with { InFrame = 100, OutFrame = 129, ExcludedFrames = [100, 110, 111], Speed = 0.5 }, 0,
            "-ss 3.319983 -t 1.001 -i in.src",
            "[0:v:0]trim=end_frame=30,select='between(n,1,9)+between(n,12,29)',setpts=N/(30000/1001*0.5)/TB,fps=30,tpad=stop=-1:stop_mode=clone,trim=end_frame=54,scale=1920:1080,setsar=1,crop=1080:1080:420:0:exact=1,scale=512:512:flags=lanczos,setsar=1,format=yuv420p[v]"
        },
        {
            "a long segment cut at 3 s", Video, Square with { OutFrame = 149 }, 0,
            "-t 2.983333 -i in.src",
            "[0:v:0]trim=end_frame=90,setpts=PTS-STARTPTS,fps=30,tpad=stop=-1:stop_mode=clone,trim=end_frame=90,scale=1920:1080,setsar=1,crop=1080:1080:420:0:exact=1,scale=512:512:flags=lanczos,setsar=1,format=yuv420p[v]"
        },
        {
            "a single frame", Video, Square with { InFrame = 75, OutFrame = 75 }, 0,
            "-ss 2.483333 -t 0.033333 -i in.src",
            "[0:v:0]trim=end_frame=1,setpts=PTS-STARTPTS,fps=30,tpad=stop=-1:stop_mode=clone,trim=end_frame=1,scale=1920:1080,setsar=1,crop=1080:1080:420:0:exact=1,scale=512:512:flags=lanczos,setsar=1,format=yuv420p[v]"
        },
        {
            "the last step of the ladder", Video, Square, -1,
            "-t 2.983333 -i in.src",
            "[0:v:0]trim=end_frame=90,setpts=PTS-STARTPTS,fps=20,tpad=stop=-1:stop_mode=clone,trim=end_frame=60,scale=1920:1080,setsar=1,crop=1080:1080:420:0:exact=1,scale=384:384:flags=lanczos,scale=512:512:flags=lanczos,setsar=1,format=yuv420p[v]"
        },
        {
            "a ping-pong at the ladder's 24 fps", Video, Square with { Loop = LoopMode.Pingpong }, 1,
            "-t 1.516667 -i in.src",
            "[0:v:0]trim=end_frame=46,setpts=PTS-STARTPTS,fps=24,tpad=stop=-1:stop_mode=clone,trim=end_frame=37,scale=1920:1080,setsar=1,crop=1080:1080:420:0:exact=1,scale=512:512:flags=lanczos,setsar=1,split[forth][back];"
            + "[back]reverse,trim=start_frame=1:end_frame=36,setpts=PTS-STARTPTS[backward];"
            + "[forth][backward]concat=n=2:v=1:a=0,format=yuv420p[v]"
        },
    };

    private static StickerRender Render(MediaInfo source, Segment segment, int step)
    {
        var spec = SceneCompiler.Compile(new Scene(Scene.CurrentSchemaVersion, [new Layer.Video("video")], [segment]), segment.Id, source);
        var rungs = FitLadder.Rungs(spec, LadderProfile.Sticker);
        var rung = step < 0 ? rungs[^1] : rungs[step];
        return new StickerRender("in.src", source, spec.At(rung), new RateControl.ConstantQuality(rung.Crf), RenderEffort.Final, "out.webm");
    }

    [Theory]
    [MemberData(nameof(Scenes))]
    public void Compiles_each_scene_into_its_filtergraph(string name, MediaInfo source, Segment segment, int step, string input, string graph)
    {
        var render = Render(source, segment, step);

        string.Join(' ', StickerFilterGraph.Input(render)).Should().Be(input, name);
        StickerFilterGraph.Filters(render).Should().Be(graph, name);
    }

    [Fact]
    public void Holds_at_least_25_scenes()
    {
        Scenes.Should().HaveCountGreaterThanOrEqualTo(25);
    }

    [Fact]
    public void Passes_each_input_option_as_an_argument_of_its_own()
    {
        var render = Render(Video, Square with { InFrame = 40, OutFrame = 99 }, 0) with { SourcePath = "D:/Видео/кот с пробелом.mp4" };

        StickerFilterGraph.Input(render).Should().Equal("-ss", "1.316667", "-t", "2", "-i", "D:/Видео/кот с пробелом.mp4");
    }
}
