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
        _ffmpeg.RunAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<IProgress<FfmpegProgress>?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var arguments = call.Arg<IReadOnlyList<string>>();
            _runs.Add(arguments);
            if (arguments[^1].EndsWith(".webm")) File.WriteAllBytes(arguments[^1], new byte[1000]);
            var progress = call.Arg<IProgress<FfmpegProgress>?>();
            progress?.Report(new FfmpegProgress(45, null, null, IsEnd: false));
            progress?.Report(new FfmpegProgress(90, null, null, IsEnd: true));
            return new FfmpegResult(0, "", "");
        });
    }

    /// <summary>ffmpeg writes part of the file, then fails or is stopped as <paramref name="ends"/> says.</summary>
    private void Fails(Func<FfmpegResult> ends) =>
        _ffmpeg.RunAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<IProgress<FfmpegProgress>?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var arguments = call.Arg<IReadOnlyList<string>>();
            _runs.Add(arguments);
            if (arguments[^1].EndsWith(".webm")) File.WriteAllBytes(arguments[^1], new byte[500]);
            return ends();
        });

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

        var sticker = await new FfmpegStickerRenderer(_ffmpeg).RenderAsync(render, progress: null, Cancel);

        sticker.Should().Be(new RenderedSticker(render.OutputPath, 1000));
        _runs.Should().ContainSingle().Which.Should().ContainInConsecutiveOrder("-crf", "31");
    }

    [Fact]
    public async Task Runs_two_passes_over_one_statistics_file_and_then_removes_it()
    {
        await new FfmpegStickerRenderer(_ffmpeg).RenderAsync(Render(new RateControl.TwoPass(664_098)), progress: null, Cancel);

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
        Fails(() => new FfmpegResult(1, "", "Error while opening encoder", "ffmpeg -i cat.gif -pass 1"));

        var render = () => new FfmpegStickerRenderer(_ffmpeg).RenderAsync(Render(new RateControl.TwoPass(664_098)), progress: null, Cancel);

        var error = (await render.Should().ThrowAsync<AppException>()).Which;
        error.Code.Should().Be(ErrorCode.RenderFailed);
        error.Log.Should().Be("Error while opening encoder");
        error.Context.Should().Contain("command", "ffmpeg -i cat.gif -pass 1");
        _runs.Should().ContainSingle();
    }

    [Fact]
    public async Task Reports_each_pass_from_the_frames_written_over_the_frames_of_the_sticker()
    {
        var passes = new CollectProgress<PassProgress>();
        var render = Render(new RateControl.TwoPass(664_098));

        await new FfmpegStickerRenderer(_ffmpeg).RenderAsync(render, passes, Cancel);

        render.Spec.PlayedFrames.Should().Be(90);
        passes.Reports.Should().Equal(
            new PassProgress(1, 2, 0), new PassProgress(1, 2, 0.5), new PassProgress(1, 2, 1), new PassProgress(1, 2, 1),
            new PassProgress(2, 2, 0), new PassProgress(2, 2, 0.5), new PassProgress(2, 2, 1), new PassProgress(2, 2, 1));
    }

    [Fact]
    public async Task Asks_ffmpeg_for_no_progress_when_nobody_listens()
    {
        await new FfmpegStickerRenderer(_ffmpeg).RenderAsync(Render(new RateControl.ConstantQuality(40)), progress: null, Cancel);

        await _ffmpeg.Received(1).RunAsync(Arg.Any<IReadOnlyList<string>>(), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Calls_a_disk_that_filled_up_during_a_pass_disk_full_and_leaves_no_half_file()
    {
        Fails(() => new FfmpegResult(1, "", "[out#0/webm] Error writing trailer: No space left on device"));
        var render = Render(new RateControl.ConstantQuality(40));

        var run = () => new FfmpegStickerRenderer(_ffmpeg).RenderAsync(render, progress: null, Cancel);

        var error = (await run.Should().ThrowAsync<AppException>()).Which;
        error.Code.Should().Be(ErrorCode.DiskFull);
        error.Log.Should().Contain("No space left on device");
        File.Exists(render.OutputPath).Should().BeFalse();
    }

    [Fact]
    public async Task Leaves_neither_the_half_file_nor_the_statistics_when_cancelled_in_the_second_pass()
    {
        Fails(() => throw new OperationCanceledException());
        var render = Render(new RateControl.TwoPass(664_098));
        _ffmpeg.RunAsync(Arg.Is<IReadOnlyList<string>>(a => a.Last() == "-"), Arg.Any<IProgress<FfmpegProgress>?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _runs.Add(call.Arg<IReadOnlyList<string>>());
                return new FfmpegResult(0, "", "");
            });

        var run = () => new FfmpegStickerRenderer(_ffmpeg).RenderAsync(render, progress: null, Cancel);

        await run.Should().ThrowAsync<OperationCanceledException>();
        _runs.Should().HaveCount(2);
        File.Exists(render.OutputPath).Should().BeFalse();
        var log = _runs[0][_runs[0].ToList().IndexOf("-passlogfile") + 1];
        Directory.Exists(Path.GetDirectoryName(log)).Should().BeFalse();
    }

    [Fact]
    public async Task Keeps_a_draft_at_the_path_asked_with_the_estimate_of_the_final_render()
    {
        var spec = StickerSpec.Default(Media.Landscape, TelegramLimits.Current.Sticker);
        var path = Path.Combine(_output.FullName, "preview.webm");

        var draft = await new FfmpegStickerRenderer(_ffmpeg).DraftAsync(new StickerProbe("in/cat.mp4", Media.Landscape, spec, 40), path, Cancel);

        draft.Should().Be(new DraftSticker(path, 1000, 820));
        File.Exists(path).Should().BeTrue();
        _runs.Should().ContainSingle().Which.Should().ContainInConsecutiveOrder("-deadline", "realtime");
    }

    [Fact]
    public void Describes_two_passes_as_two_commands_without_running_them()
    {
        var render = Render(new RateControl.TwoPass(664_098)) with { SourcePath = "D:/Видео/кот 1.mp4" };

        var lines = new FfmpegStickerRenderer(_ffmpeg).Describe(render).Split(Environment.NewLine);

        lines.Should().HaveCount(2).And.AllSatisfy(line => line.Should().StartWith("ffmpeg -t 2.983333 -i \"D:/Видео/кот 1.mp4\" -filter_complex \"[0:v:0]"));
        lines[0].Should().Contain("-pass 1").And.EndWith("-f null -");
        lines[1].Should().Contain("-pass 2").And.EndWith($"-f webm -y {render.OutputPath}");
        _runs.Should().BeEmpty();
    }
}
