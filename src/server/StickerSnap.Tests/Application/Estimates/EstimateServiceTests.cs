using StickerSnap.Application.Estimates;
using StickerSnap.Application.Media;
using StickerSnap.Application.Projects;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Render;
using StickerSnap.Tests.Infrastructure.Projects;

namespace StickerSnap.Tests.Application.Estimates;

/// <summary>
/// The size forecast over a media engine that only names a size per step of the ladder: what is
/// measured, what the inspector is told, and what is answered from memory.
/// </summary>
public sealed class EstimateServiceTests : IDisposable
{
    private static readonly Project Cat = SampleProjects.Sample;

    /// <summary>30 frames of the 10 fps GIF, cropped to a square: 512×512 at 10 fps for 3 s.</summary>
    private static readonly Segment Sticker = Cat.Scene.Segments[0];

    private readonly IProjectService _projects = Substitute.For<IProjectService>();
    private readonly IStickerRenderer _renderer = Substitute.For<IStickerRenderer>();
    private readonly List<StickerProbe> _probes = [];
    private readonly EstimateService _service;

    /// <summary>
    /// The final file of the 30 frames at each CRF of the ladder, less for fewer frames in
    /// proportion over <see cref="_fixedBytes"/>; the detail steps take 4% off CRF 52.
    /// </summary>
    private Dictionary<int, long> _bytesByCrf = new() { [40] = 180_000, [46] = 120_000, [52] = 80_000 };

    /// <summary>Bytes a sticker weighs whatever its length, like the first frame of a clip.</summary>
    private long _fixedBytes;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public EstimateServiceTests()
    {
        Opens(PresenceStatus.Ok);
        _renderer.EstimateAsync(Arg.Any<StickerProbe>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var probe = call.Arg<StickerProbe>();
            _probes.Add(probe);
            var whole = probe.Spec.Detail is null ? _bytesByCrf[probe.Crf] : (long)(_bytesByCrf[probe.Crf] * 0.96);
            return Task.FromResult(_fixedBytes + (whole - _fixedBytes) * probe.Spec.PlayedFrames / 30);
        });
        _service = new EstimateService(_projects, _renderer);
    }

    public void Dispose() => _service.Dispose();

    private void Opens(PresenceStatus source) =>
        _projects.GetAsync(Cat.Id, Arg.Any<CancellationToken>()).Returns(new OpenedProject(Cat, "/projects/cat.ssproj", source));

    private Task<SegmentEstimate> EstimateAsync(Segment segment) => _service.EstimateAsync(Cat.Id, segment, Cancel);

    /// <summary>The steps of the ladder measured for the whole segment, not for a suggested edit.</summary>
    private IEnumerable<StickerProbe> LadderProbes => _probes.Where(probe => probe.Spec.PlayedFrames == 30);

    [Fact]
    public async Task Measures_the_first_step_of_the_ladder_from_the_project_s_source()
    {
        var estimate = await EstimateAsync(Sticker);

        var probe = _probes.Should().ContainSingle().Which;
        (probe.SourcePath, probe.Crf, probe.Spec.Fps, probe.Spec.Size, probe.Spec.Detail)
            .Should().Be((Cat.Source.Path, 40, 10, new PixelSize(512, 512), (PixelSize?)null));
        estimate.Should().BeEquivalentTo(new SegmentEstimate(180_000, 262_144, true, new StickerOutput(512, 512, 10, 3), [], []));
    }

    [Fact]
    public async Task Tells_the_length_of_a_ping_pong_both_ways()
    {
        var estimate = await EstimateAsync(Sticker with { OutFrame = 9, Loop = LoopMode.Pingpong });

        estimate.Output.DurationSec.Should().Be(1.8);
    }

    [Fact]
    public async Task Climbs_the_ladder_like_the_export_to_say_what_will_likely_be_given_up()
    {
        _bytesByCrf = new() { [40] = 300_000, [46] = 240_000, [52] = 160_000 };

        var estimate = await EstimateAsync(Sticker);

        LadderProbes.Select(probe => probe.Crf).Should().Equal(40, 46);
        (estimate.Bytes, estimate.Fits).Should().Be((300_000, false));
        estimate.LikelyConcessions.Should().Equal(new Concession.Crf(40, 46));
    }

    [Fact]
    public async Task Names_the_last_step_s_concessions_when_no_step_fits()
    {
        _bytesByCrf = new() { [40] = 900_000, [46] = 800_000, [52] = 700_000 };

        var estimate = await EstimateAsync(Sticker);

        LadderProbes.Should().HaveCount(5);
        estimate.LikelyConcessions.Should().Equal(new Concession.Crf(40, 52), new Concession.Scale(512, 384));
    }

    [Fact]
    public async Task Counts_a_sticker_over_the_budget_but_under_the_limit_as_over_when_the_ladder_fits_it()
    {
        // The export steps down when the first step is over 95% of the limit
        _bytesByCrf = new() { [40] = 255_000, [46] = 170_000, [52] = 110_000 };

        var estimate = await EstimateAsync(Sticker);

        (estimate.Fits, estimate.LikelyConcessions.Count).Should().Be((false, 1));
    }

    [Fact]
    public async Task Measures_only_the_first_step_with_fitting_off_and_holds_it_to_the_limit()
    {
        _bytesByCrf = new() { [40] = 255_000, [46] = 170_000, [52] = 110_000 };

        var asIs = await EstimateAsync(Sticker with { AutoFit = false });

        (asIs.Bytes, asIs.Fits, asIs.LikelyConcessions.Count, asIs.Suggestions.Count).Should().Be((255_000, true, 0, 0));
        _probes.Should().ContainSingle();
    }

    [Fact]
    public async Task Suggests_edits_only_when_the_sticker_is_over()
    {
        _bytesByCrf = new() { [40] = 400_000, [46] = 240_000, [52] = 160_000 };

        var over = await EstimateAsync(Sticker with { AutoFit = false });

        over.Fits.Should().BeFalse();
        over.LikelyConcessions.Should().BeEmpty();
        // 13 333 B a frame of the 30: 19 frames fit in 262 144 B
        over.Suggestions.Should().Equal(new SizeSuggestion.Trim(18, 1.9));
    }

    [Fact]
    public async Task Works_out_again_a_suggestion_its_draft_finds_over()
    {
        // 360 000 B for 30 frames, of which 60 000 B whatever the length: by the proportion 20 frames
        // fit, but weigh 260 000 B; from that 19, which weigh 250 000 B; from that 18, which fit
        _fixedBytes = 60_000;
        _bytesByCrf = new() { [40] = 360_000, [46] = 240_000, [52] = 160_000 };

        var over = await EstimateAsync(Sticker);

        over.Suggestions.Should().Equal(new SizeSuggestion.Trim(17, 1.8));
        _probes.Where(probe => probe.Spec.PlayedFrames < 30).Select(probe => probe.Spec.PlayedFrames).Should().Equal(20, 19, 18);
    }

    [Fact]
    public async Task Does_not_offer_a_suggestion_that_stays_over_after_its_drafts()
    {
        _fixedBytes = 300_000;
        _bytesByCrf = new() { [40] = 300_000, [46] = 240_000, [52] = 160_000 };

        var over = await EstimateAsync(Sticker);

        over.Suggestions.Should().BeEmpty();
        // The trim is drafted three times; 4× (23 frames) once, since nothing is faster
        var trims = _probes.Where(probe => probe.Spec.PlayedFrames < 30 && probe.Spec.Clip.Speed == 1);
        trims.Should().HaveCount(EstimateService.MeasuresPerSuggestion);
    }

    [Fact]
    public async Task Answers_the_same_segment_from_memory_whatever_its_name()
    {
        await EstimateAsync(Sticker);
        var again = await EstimateAsync(Sticker with { Name = "Другое имя" });

        again.Bytes.Should().Be(180_000);
        _probes.Should().ContainSingle();
    }

    [Fact]
    public async Task Measures_an_edited_segment_again()
    {
        await EstimateAsync(Sticker);
        await EstimateAsync(Sticker with { OutFrame = 19 });

        _probes.Should().HaveCount(2);
    }

    [Fact]
    public async Task Refuses_a_segment_that_breaks_the_rules_with_its_fields()
    {
        var estimate = () => EstimateAsync(Sticker with { OutFrame = 40, Speed = 5 });

        var error = (await estimate.Should().ThrowAsync<AppException>()).Which;
        error.Code.Should().Be(ErrorCode.BadRequest);
        error.Fields.Should().BeEquivalentTo(new Dictionary<string, string[]>
        {
            ["segment.outFrame"] = [SceneRules.OutOfRange],
            ["segment.speed"] = [SceneRules.OutOfRange],
        });
        _probes.Should().BeEmpty();
    }

    [Fact]
    public async Task Says_the_video_is_gone()
    {
        Opens(PresenceStatus.Missing);

        var estimate = () => EstimateAsync(Sticker);

        (await estimate.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.SourceNotFound);
    }

    [Fact]
    public async Task Measures_one_segment_at_a_time_and_drops_a_request_given_up_while_it_waits()
    {
        var first = new TaskCompletionSource<long>();
        _renderer.EstimateAsync(Arg.Any<StickerProbe>(), Arg.Any<CancellationToken>()).Returns(first.Task, Task.FromResult(150_000L));
        using var givenUp = new CancellationTokenSource();

        var measuring = EstimateAsync(Sticker);
        var waiting = _service.EstimateAsync(Cat.Id, Sticker with { OutFrame = 19 }, givenUp.Token);
        await givenUp.CancelAsync();
        await waiting.Invoking(task => task).Should().ThrowAsync<OperationCanceledException>();
        first.SetResult(170_000);

        (await measuring).Bytes.Should().Be(170_000);
        await _renderer.Received(1).EstimateAsync(Arg.Any<StickerProbe>(), Arg.Any<CancellationToken>());
    }
}
