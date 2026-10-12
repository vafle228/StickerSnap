using StickerSnap.Application.Media;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Render;
using StickerSnap.Core.Telegram;
using StickerSnap.Infrastructure.Ffmpeg;
using StickerSnap.Tests.Core.Render;

namespace StickerSnap.Tests.Infrastructure.Ffmpeg;

/// <summary>The runs the renderer asks of a substituted ffmpeg, which writes a file of a set size.</summary>
public sealed class FfmpegStickerRendererTests : IDisposable
{
    private readonly DirectoryInfo _output = Directory.CreateTempSubdirectory("stickersnap-render-");
    private readonly IFfmpeg _ffmpeg = Substitute.For<IFfmpeg>();
    private readonly List<IReadOnlyList<string>> _runs = [];

    public FfmpegStickerRendererTests()
    {
        _ffmpeg.RunAsync(Arg.Any<IReadOnlyList<string>>(), null, Arg.Any<CancellationToken>()).Returns(call =>
        {
            var arguments = call.Arg<IReadOnlyList<string>>();
            _runs.Add(arguments);
            if (arguments[^1].EndsWith(".webm")) File.WriteAllBytes(arguments[^1], new byte[1000]);
            return new FfmpegResult(0, "", "");
        });
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public void Dispose() => _output.Delete(recursive: true);

    private StickerRender Render(RateControl rate) => new(
        "in/cat.mp4",
        Media.Landscape,
        StickerSpec.Default(Media.Landscape, TelegramLimits.Current.Sticker),
        rate,
        RenderEffort.Final,
        Path.Combine(_output.FullName, "stickers", "cat.webm"));

    [Fact]
    public async Task Renders_a_crf_sticker_in_one_run_and_reports_its_size()
    {
        var render = Render(new RateControl.ConstantQuality(31));

        var sticker = await new FfmpegStickerRenderer(_ffmpeg).RenderAsync(render, Cancel);

        sticker.Should().Be(new RenderedSticker(render.OutputPath, 1000));
        _runs.Should().ContainSingle().Which.Should().ContainInConsecutiveOrder("-crf", "31");
    }

    [Fact]
    public async Task Runs_two_passes_over_one_statistics_file_and_then_removes_it()
    {
        await new FfmpegStickerRenderer(_ffmpeg).RenderAsync(Render(new RateControl.TwoPass(664_098)), Cancel);

        _runs.Should().HaveCount(2);
        var log = _runs[0][_runs[0].ToList().IndexOf("-passlogfile") + 1];
        _runs[0].Should().ContainInConsecutiveOrder("-pass", "1", "-passlogfile", log);
        _runs[1].Should().ContainInConsecutiveOrder("-pass", "2", "-passlogfile", log);
        Directory.Exists(Path.GetDirectoryName(log)).Should().BeFalse();
    }

    [Theory]
    [InlineData(false, 820)]
    [InlineData(true, 633)]
    public async Task Estimates_the_final_size_at_a_crf_from_a_draft_it_then_removes(bool transparent, long bytes)
    {
        var spec = StickerSpec.Default(Media.Landscape, TelegramLimits.Current.Sticker) with { KeepAlpha = transparent };

        var estimate = await new FfmpegStickerRenderer(_ffmpeg).EstimateAsync(new StickerProbe("in/cat.mp4", Media.Landscape, spec, 37), Cancel);

        estimate.Should().Be(bytes);
        var run = _runs.Should().ContainSingle().Which;
        run.Should().ContainInConsecutiveOrder("-crf", "37");
        run.Should().ContainInConsecutiveOrder("-deadline", "realtime");
        Directory.Exists(Path.GetDirectoryName(run[^1])).Should().BeFalse();
    }

    [Fact]
    public async Task Fails_with_the_log_of_ffmpeg_and_stops_after_a_failed_pass()
    {
        _ffmpeg.RunAsync(Arg.Any<IReadOnlyList<string>>(), null, Arg.Any<CancellationToken>()).Returns(call =>
        {
            _runs.Add(call.Arg<IReadOnlyList<string>>());
            return new FfmpegResult(1, "", "Error while opening encoder");
        });

        var render = () => new FfmpegStickerRenderer(_ffmpeg).RenderAsync(Render(new RateControl.TwoPass(664_098)), Cancel);

        var error = (await render.Should().ThrowAsync<AppException>()).Which;
        error.Code.Should().Be(ErrorCode.RenderFailed);
        error.Log.Should().Be("Error while opening encoder");
        _runs.Should().ContainSingle();
    }
}
