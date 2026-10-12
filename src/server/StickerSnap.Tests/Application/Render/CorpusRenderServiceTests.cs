using Microsoft.Extensions.Time.Testing;
using StickerSnap.Application.Media;
using StickerSnap.Application.Render;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;
using StickerSnap.Core.Render;
using StickerSnap.Core.Telegram;
using StickerSnap.Tests.Core.Render;

namespace StickerSnap.Tests.Application.Render;

public sealed class CorpusRenderServiceTests
{
    private static readonly RenderMode Crf = new("crf", RenderEffort.Final, 31);
    private static readonly RenderMode TwoPass = new("2pass", RenderEffort.Final, null);

    private readonly IMediaProbe _probe = Substitute.For<IMediaProbe>();
    private readonly IStickerRenderer _renderer = Substitute.For<IStickerRenderer>();
    private readonly FakeTimeProvider _time = new();
    private readonly CorpusRenderService _service;

    public CorpusRenderServiceTests()
    {
        _service = new CorpusRenderService(_probe, _renderer, new StickerFitService(_renderer), _time);
        _probe.ProbeAsync("corpus/cat.mp4", Arg.Any<CancellationToken>()).Returns(Media.Landscape);
        _probe.ProbeAsync(Arg.Is<string>(p => p.EndsWith(".webm")), Arg.Any<CancellationToken>()).Returns(Media.Sticker);
        _renderer.RenderAsync(Arg.Any<StickerRender>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _time.Advance(TimeSpan.FromSeconds(2));
            return new RenderedSticker(call.Arg<StickerRender>().OutputPath, 250_000);
        });
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Renders_the_default_sticker_of_a_clip_in_each_mode()
    {
        await _service.RunAsync(["corpus/cat.mp4"], "out", [Crf, TwoPass], null, Cancel);

        var spec = StickerSpec.Default(Media.Landscape, TelegramLimits.Current.Sticker);
        await _renderer.Received(1).RenderAsync(
            new StickerRender("corpus/cat.mp4", Media.Landscape, spec, new RateControl.ConstantQuality(31), RenderEffort.Final,
                Path.Combine("out", "cat.mp4.crf.webm")),
            Arg.Any<CancellationToken>());
        await _renderer.Received(1).RenderAsync(
            new StickerRender("corpus/cat.mp4", Media.Landscape, spec, new RateControl.TwoPass(664_098), RenderEffort.Final,
                Path.Combine("out", "cat.mp4.2pass.webm")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reports_the_size_the_time_and_what_the_probe_read_from_each_sticker()
    {
        var report = await _service.RunAsync(["corpus/cat.mp4"], "out", [Crf], null, Cancel);

        var outcome = report.Entries.Single().Outcomes.Single();
        outcome.Should().BeEquivalentTo(new
        {
            Mode = Crf,
            Elapsed = TimeSpan.FromSeconds(2),
            SizeBytes = 250_000L,
            Sticker = Media.Sticker,
            Problems = Array.Empty<string>(),
            Failure = (Failure?)null,
        });
        outcome.Passes.Should().BeTrue();
    }

    [Fact]
    public async Task Names_what_Telegram_would_reject_in_a_sticker()
    {
        _probe.ProbeAsync(Arg.Is<string>(p => p.EndsWith(".webm")), Arg.Any<CancellationToken>())
            .Returns(Media.Sticker with { SizeBytes = 300_000 });

        var report = await _service.RunAsync(["corpus/cat.mp4"], "out", [Crf], null, Cancel);

        var outcome = report.Entries.Single().Outcomes.Single();
        outcome.Problems.Should().Equal("300000 B is over 262144 B");
        outcome.Passes.Should().BeFalse();
    }

    [Fact]
    public async Task Tells_a_sticker_that_lost_the_alpha_of_its_source()
    {
        _probe.ProbeAsync("corpus/cat.mp4", Arg.Any<CancellationToken>()).Returns(Media.Landscape with { HasAlpha = true });

        var report = await _service.RunAsync(["corpus/cat.mp4"], "out", [Crf], null, Cancel);

        var entry = report.Entries.Single();
        entry.Outcomes.Single().KeptAlpha(entry.Source!).Should().BeFalse();
    }

    [Fact]
    public async Task Splits_the_two_pass_budget_between_the_picture_and_the_alpha_of_a_transparent_clip()
    {
        _probe.ProbeAsync("corpus/cat.mp4", Arg.Any<CancellationToken>()).Returns(Media.Landscape with { HasAlpha = true });

        await _service.RunAsync(["corpus/cat.mp4"], "out", [TwoPass], null, Cancel);

        await _renderer.Received(1).RenderAsync(
            Arg.Is<StickerRender>(r => r.Rate == new RateControl.TwoPass(332_049)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Goes_down_the_fit_ladder_in_a_fit_mode_and_reports_how()
    {
        _renderer.EstimateAsync(Arg.Any<StickerProbe>(), Arg.Any<CancellationToken>()).Returns(300_000L, 200_000L);

        var report = await _service.RunAsync(["corpus/cat.mp4"], "out", [new RenderMode("fit", RenderEffort.Final, null, Fit: true)], null, Cancel);

        var outcome = report.Entries.Single().Outcomes.Single();
        outcome.Fitted!.Concessions.Should().Equal(new Concession.Fps(30, 24));
        outcome.Rate.Should().Be(new RateControl.TwoPass(664_098));
        outcome.SizeBytes.Should().Be(250_000);
        await _renderer.Received(1).RenderAsync(
            Arg.Is<StickerRender>(r => r.Spec.Fps == 24 && r.OutputPath == Path.Combine("out", "cat.mp4.fit.webm")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Goes_on_past_a_clip_the_probe_cannot_read()
    {
        _probe.ProbeAsync("corpus/broken.mp4", Arg.Any<CancellationToken>())
            .Returns<MediaInfo>(_ => throw new AppException(ErrorCode.SourceUnreadable, "cannot read") { Log = "moov atom not found" });

        var report = await _service.RunAsync(["corpus/broken.mp4", "corpus/cat.mp4"], "out", [Crf], null, Cancel);

        report.Entries[0].Should().BeEquivalentTo(new
        {
            Source = (MediaInfo?)null,
            Outcomes = Array.Empty<ModeOutcome>(),
            Failure = new Failure("SourceUnreadable: cannot read", "moov atom not found"),
        });
        report.Entries[1].Outcomes.Single().Passes.Should().BeTrue();
    }

    [Fact]
    public async Task Goes_on_to_the_next_mode_when_a_render_fails()
    {
        _renderer.RenderAsync(Arg.Is<StickerRender>(r => r.Rate is RateControl.ConstantQuality), Arg.Any<CancellationToken>())
            .Returns<RenderedSticker>(_ => throw new AppException(ErrorCode.RenderFailed, "ffmpeg failed") { Log = "Invalid argument" });

        var report = await _service.RunAsync(["corpus/cat.mp4"], "out", [Crf, TwoPass], null, Cancel);

        var outcomes = report.Entries.Single().Outcomes;
        outcomes[0].Should().BeEquivalentTo(new
        {
            SizeBytes = (long?)null,
            Failure = new Failure("RenderFailed: ffmpeg failed", "Invalid argument"),
        });
        outcomes[0].Passes.Should().BeFalse();
        outcomes[1].Passes.Should().BeTrue();
    }

    [Fact]
    public async Task Reports_each_clip_as_soon_as_it_is_done()
    {
        var reported = new List<string>();

        await _service.RunAsync(["corpus/cat.mp4"], "out", [Crf], new Recorder(e => reported.Add(e.SourcePath)), Cancel);

        reported.Should().Equal("corpus/cat.mp4");
    }

    private sealed class Recorder(Action<CorpusEntry> report) : IProgress<CorpusEntry>
    {
        public void Report(CorpusEntry value) => report(value);
    }
}
