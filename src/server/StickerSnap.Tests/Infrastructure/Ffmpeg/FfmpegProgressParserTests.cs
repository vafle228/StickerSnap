using StickerSnap.Application.Ffmpeg;
using StickerSnap.Infrastructure.Ffmpeg;

namespace StickerSnap.Tests.Infrastructure.Ffmpeg;

public sealed class FfmpegProgressParserTests
{
    private readonly List<FfmpegProgress> _reports = [];

    private FfmpegProgressParser Parser() => new(new Recorder(_reports));

    private static void Feed(FfmpegProgressParser parser, string block)
    {
        foreach (var line in block.Split('\n')) parser.Add(line);
    }

    [Fact]
    public void Reports_each_block_once_when_it_ends()
    {
        var parser = Parser();

        Feed(parser, """
            frame=12
            fps=0.00
            out_time_us=400000
            out_time=00:00:00.400000
            speed=0.8x
            progress=continue
            frame=30
            out_time_us=1000000
            speed=1.95x
            progress=end
            """);

        _reports.Should().Equal(
            new FfmpegProgress(12, TimeSpan.FromMilliseconds(400), 0.8, IsEnd: false),
            new FfmpegProgress(30, TimeSpan.FromSeconds(1), 1.95, IsEnd: true));
    }

    [Fact]
    public void Knows_no_time_or_speed_while_ffmpeg_says_n_a()
    {
        var parser = Parser();

        Feed(parser, """
            frame=0
            out_time_us=N/A
            speed=N/A
            progress=continue
            """);

        _reports.Should().ContainSingle().Which.Should().Be(new FfmpegProgress(0, null, null, IsEnd: false));
    }

    [Fact]
    public void Skips_lines_that_are_not_key_value()
    {
        var parser = Parser();

        Feed(parser, "\nnonsense\n=1\nprogress=continue");

        _reports.Should().ContainSingle();
    }

    /// <summary>Reports at once on the calling thread, unlike Progress&lt;T&gt;.</summary>
    private sealed class Recorder(List<FfmpegProgress> reports) : IProgress<FfmpegProgress>
    {
        public void Report(FfmpegProgress value) => reports.Add(value);
    }
}
