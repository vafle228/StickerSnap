using StickerSnap.Application.Media;
using StickerSnap.Application.Render;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Render;
using StickerSnap.Core.Validation;
using StickerSnap.Tests.Core.Render;

namespace StickerSnap.Tests.Application.Render;

/// <summary>
/// The render engine over a substituted renderer that answers with set sizes and a probe that
/// reads back a sticker of the size written.
/// </summary>
public sealed class SegmentRenderServiceTests
{
    private const string Output = "out/seg-1.webm";

    private static readonly Segment Segment = new(
        "seg-1", "Стикер 1", InFrame: 0, OutFrame: 89, ExcludedFrames: [],
        new SegmentFrame.Crop(CropPreset.Square, new FrameRect(420, 0, 1080, 1080)),
        Speed: 1, LoopMode.Normal, SegmentPreset.Sticker, LongSticker: false, AutoFit: true);

    private readonly IStickerRenderer _renderer = Substitute.For<IStickerRenderer>();
    private readonly IMediaProbe _probe = Substitute.For<IMediaProbe>();
    private readonly List<StickerRender> _renders = [];
    private readonly List<StickerProbe> _drafts = [];
    private readonly SegmentRenderService _service;
    private MediaInfo _written = Media.Sticker;

    public SegmentRenderServiceTests()
    {
        _service = new SegmentRenderService(new StickerFitService(_renderer), _renderer, _probe);
        Estimates(200_000);
        Renders(240_000);
        Drafts(200_000);
        _probe.ProbeAsync(Output, Arg.Any<CancellationToken>()).Returns(_ => _written);
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static SegmentRender Request(Segment segment, RenderEffort quality = RenderEffort.Final) =>
        new("in/cat.mp4", Media.Landscape, SceneOf(segment), segment.Id, quality, Output);

    private static Scene SceneOf(params Segment[] segments) => new(Scene.CurrentSchemaVersion, [new Layer.Video("video")], segments);

    private void Estimates(params long[] bytes)
    {
        var next = 0;
        _renderer.EstimateAsync(Arg.Any<StickerProbe>(), Arg.Any<CancellationToken>())
            .Returns(_ => bytes[Math.Min(next++, bytes.Length - 1)]);
    }

    /// <summary>Each render writes a file of the next size, reporting both passes, and the probe reads it back.</summary>
    private void Renders(params long[] bytes)
    {
        var next = 0;
        _renderer.RenderAsync(Arg.Any<StickerRender>(), Arg.Any<IProgress<PassProgress>?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var render = call.Arg<StickerRender>();
            _renders.Add(render);
            var size = bytes[Math.Min(next++, bytes.Length - 1)];
            var passes = render.Rate is RateControl.TwoPass ? 2 : 1;
            for (var pass = 1; pass <= passes; pass++)
            {
                call.Arg<IProgress<PassProgress>?>()?.Report(new PassProgress(pass, passes, 0.5));
                call.Arg<IProgress<PassProgress>?>()?.Report(new PassProgress(pass, passes, 1));
            }

            _written = Media.Sticker with
            {
                Width = render.Spec.Width,
                Height = render.Spec.Height,
                SizeBytes = size,
                Fps = new(render.Spec.Fps, 1),
                FrameCount = render.Spec.PlayedFrames,
                DurationSec = render.Spec.DurationSec,
            };
            return new RenderedSticker(render.OutputPath, size);
        });
    }

    /// <summary>Each draft weighs a quarter more than the estimate of its final render.</summary>
    private void Drafts(params long[] estimates)
    {
        var next = 0;
        _renderer.DraftAsync(Arg.Any<StickerProbe>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _drafts.Add(call.Arg<StickerProbe>());
            var estimate = estimates[Math.Min(next++, estimates.Length - 1)];
            return new DraftSticker(call.Arg<string>(), estimate * 5 / 4, estimate);
        });
    }

    [Fact]
    public async Task Renders_a_segment_that_fits_at_the_first_step_and_checks_the_file_against_telegram()
    {
        var sticker = await _service.RenderAsync(Request(Segment), progress: null, Cancel);

        _renders.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            SourcePath = "in/cat.mp4",
            Rate = new RateControl.TwoPass(664_098),
            Effort = RenderEffort.Final,
            OutputPath = Output,
        });
        sticker.Should().BeEquivalentTo(new
        {
            SegmentId = "seg-1",
            Quality = RenderEffort.Final,
            Path = Output,
            Bytes = 240_000L,
            Output = new StickerOutput(512, 512, 30, 3),
            Crf = 40,
            Concessions = Array.Empty<Concession>(),
        });
        sticker.Ladder.Should().ContainSingle().Which.Bytes.Should().Be(240_000);
        sticker.Validation!.Verdict.Should().Be(ValidationVerdict.Pass);
        sticker.Validation.Preset.Should().Be(SegmentPreset.Sticker);
        await _probe.Received(1).ProbeAsync(Output, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Tells_what_it_gave_up_and_how_it_got_there_when_it_stepped_down()
    {
        Estimates(400_000, 300_000, 240_000);
        Renders(250_000);

        var sticker = await _service.RenderAsync(Request(Segment), progress: null, Cancel);

        sticker.Output.Should().Be(new StickerOutput(512, 512, 20, 3));
        sticker.Crf.Should().Be(40);
        sticker.Concessions.Should().Equal(new Concession.Fps(30, 20));
        sticker.Ladder.Select(step => (step.Rung.Fps, step.Bytes)).Should().Equal((30, 400_000L), (24, 300_000L), (20, 250_000L));
        sticker.Ladder[1].Rung.Concessions.Should().Equal(new Concession.Fps(30, 24));
    }

    [Fact]
    public async Task Gives_up_with_cannot_fit_and_checks_nothing_when_no_render_comes_under_the_limit()
    {
        Renders(300_000);

        var render = () => _service.RenderAsync(Request(Segment), progress: null, Cancel);

        (await render.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.CannotFit);
        await _probe.DidNotReceive().ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Renders_an_emoji_at_its_own_limits_and_checks_it_as_an_emoji()
    {
        var emoji = Segment with { Preset = SegmentPreset.Emoji };
        Renders(60_000);

        var sticker = await _service.RenderAsync(Request(emoji), progress: null, Cancel);

        sticker.Output.Should().Be(new StickerOutput(100, 100, 30, 3));
        sticker.Validation!.Preset.Should().Be(SegmentPreset.Emoji);
        sticker.Validation.Verdict.Should().Be(ValidationVerdict.Pass);
    }

    [Fact]
    public async Task Previews_a_segment_with_the_draft_of_the_step_the_export_would_take()
    {
        Drafts(400_000, 240_000);

        var preview = await _service.RenderAsync(Request(Segment, RenderEffort.Draft), progress: null, Cancel);

        _drafts.Select(d => (d.Spec.Fps, d.Crf)).Should().Equal((30, 40), (24, 40));
        _renders.Should().BeEmpty();
        preview.Should().BeEquivalentTo(new
        {
            Quality = RenderEffort.Draft,
            Path = Output,
            Bytes = 300_000L,
            Output = new StickerOutput(512, 512, 24, 3),
            Validation = (StickerValidation?)null,
        });
        preview.Ladder.Select(step => step.Bytes).Should().Equal(400_000, 240_000);
        await _probe.DidNotReceive().ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Renders_a_segment_without_fitting_once_as_it_is_and_lets_the_check_fail_on_the_size()
    {
        Renders(330_000);

        var sticker = await _service.RenderAsync(Request(Segment with { AutoFit = false }), progress: null, Cancel);

        await _renderer.DidNotReceive().EstimateAsync(Arg.Any<StickerProbe>(), Arg.Any<CancellationToken>());
        _renders.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Rate = new RateControl.ConstantQuality(40),
            Effort = RenderEffort.Final,
            OutputPath = Output,
        });
        sticker.Bytes.Should().Be(330_000);
        sticker.Concessions.Should().BeEmpty();
        sticker.Ladder.Should().ContainSingle().Which.Bytes.Should().Be(330_000);
        sticker.Validation!.Verdict.Should().Be(ValidationVerdict.Fail);
        sticker.Validation.Problems.Should().ContainSingle().Which.Should().StartWith("size 330000");
    }

    [Fact]
    public async Task Previews_a_segment_without_fitting_with_one_draft()
    {
        var preview = await _service.RenderAsync(Request(Segment with { AutoFit = false }, RenderEffort.Draft), progress: null, Cancel);

        _drafts.Should().ContainSingle().Which.Crf.Should().Be(40);
        preview.Bytes.Should().Be(250_000);
        preview.Ladder.Should().ContainSingle().Which.Bytes.Should().Be(200_000);
    }

    [Fact]
    public async Task Reports_progress_from_the_probes_through_the_passes_to_the_check_never_going_back()
    {
        Estimates(400_000, 240_000);
        Renders(270_000, 250_000);
        var progress = new CollectProgress<RenderProgress>();

        await _service.RenderAsync(Request(Segment), progress, Cancel);

        var reports = progress.Reports;
        reports[0].Should().Be(new RenderProgress(0, RenderStage.Probe, null));
        reports.Select(r => r.Fraction).Should().BeInAscendingOrder().And.EndWith(1.0).And.AllSatisfy(f => f.Should().BeInRange(0, 1));
        reports.Select(r => r.Stage).Distinct().Should().Equal(RenderStage.Probe, RenderStage.Pass1, RenderStage.Pass2, RenderStage.Validate);
        reports.Where(r => r.Stage is RenderStage.Pass1 or RenderStage.Pass2).Should().AllSatisfy(r => r.Attempt!.N.Should().Be(2));
        reports.Last(r => r.Stage == RenderStage.Pass2).Fraction.Should().BeApproximately(1 - SegmentRenderService.ValidateShare, 1e-9);
        reports.Where(r => r.Stage == RenderStage.Validate).Should().AllSatisfy(r => r.Attempt.Should().BeNull());
    }

    [Fact]
    public async Task Reports_a_preview_as_drafts_up_to_the_whole()
    {
        var progress = new CollectProgress<RenderProgress>();

        await _service.RenderAsync(Request(Segment, RenderEffort.Draft), progress, Cancel);

        progress.Reports.Select(r => (r.Stage, r.Fraction)).Should().Equal((RenderStage.Probe, 0), (RenderStage.Pass2, 0), (RenderStage.Pass2, 1));
    }

    [Fact]
    public async Task Stops_at_a_cancel_in_the_middle_of_an_attempt_and_checks_nothing()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
        _renderer.RenderAsync(Arg.Any<StickerRender>(), Arg.Any<IProgress<PassProgress>?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            call.Arg<IProgress<PassProgress>?>()?.Report(new PassProgress(1, 2, 0.5));
            cancel.Cancel();
            return Task.FromCanceled<RenderedSticker>(cancel.Token);
        });
        var progress = new CollectProgress<RenderProgress>();

        var render = () => _service.RenderAsync(Request(Segment), progress, cancel.Token);

        await render.Should().ThrowAsync<OperationCanceledException>();
        await _probe.DidNotReceive().ProbeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        progress.Reports.Should().NotContain(r => r.Stage == RenderStage.Validate);
    }

    [Fact]
    public async Task Turns_down_a_segment_the_scene_does_not_have()
    {
        var request = Request(Segment) with { SegmentId = "seg-9" };

        var render = () => _service.RenderAsync(request, progress: null, Cancel);

        (await render.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.BadRequest);
        await _renderer.DidNotReceive().EstimateAsync(Arg.Any<StickerProbe>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Renders_only_the_segment_asked_for_even_when_another_one_breaks_a_rule()
    {
        var broken = Segment with { Id = "seg-2", OutFrame = 400 };
        var request = Request(Segment) with { Scene = SceneOf(Segment, broken) };

        var sticker = await _service.RenderAsync(request, progress: null, Cancel);

        sticker.SegmentId.Should().Be("seg-1");
    }
}
