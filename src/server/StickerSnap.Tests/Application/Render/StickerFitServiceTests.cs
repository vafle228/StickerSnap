using StickerSnap.Application.Media;
using StickerSnap.Application.Render;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Render;
using StickerSnap.Core.Telegram;
using StickerSnap.Tests.Core.Render;

namespace StickerSnap.Tests.Application.Render;

/// <summary>The fit ladder over a substituted renderer that answers with set sizes.</summary>
public sealed class StickerFitServiceTests
{
    private static readonly PresetLimits Sticker = TelegramLimits.Current.Sticker;
    private static readonly StickerSpec Spec = StickerSpec.Default(Media.Landscape, Sticker);
    private static readonly StickerFit Fit = new("in/cat.mp4", Media.Landscape, Spec, Sticker, LadderProfile.Sticker, "out/cat.webm");

    private readonly IStickerRenderer _renderer = Substitute.For<IStickerRenderer>();
    private readonly List<StickerProbe> _probes = [];
    private readonly List<StickerRender> _renders = [];
    private readonly StickerFitService _service;

    public StickerFitServiceTests()
    {
        _service = new StickerFitService(_renderer);
        Estimates(200_000);
        Renders(240_000);
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private void Estimates(params long[] bytes)
    {
        var next = 0;
        _renderer.EstimateAsync(Arg.Any<StickerProbe>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _probes.Add(call.Arg<StickerProbe>());
            return bytes[Math.Min(next++, bytes.Length - 1)];
        });
    }

    private void Renders(params long[] bytes)
    {
        var next = 0;
        _renderer.RenderAsync(Arg.Any<StickerRender>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var render = call.Arg<StickerRender>();
            _renders.Add(render);
            return new RenderedSticker(render.OutputPath, bytes[Math.Min(next++, bytes.Length - 1)]);
        });
    }

    [Fact]
    public async Task Renders_the_first_step_in_two_passes_at_the_target_bitrate_when_it_fits_at_crf_40()
    {
        var sticker = await _service.FitAsync(Fit, Cancel);

        _probes.Should().ContainSingle().Which.Should().Be(new StickerProbe("in/cat.mp4", Media.Landscape, Spec, 40));
        _renders.Should().ContainSingle().Which.Should().Be(
            new StickerRender("in/cat.mp4", Media.Landscape, Spec, new RateControl.TwoPass(664_098), RenderEffort.Final, "out/cat.webm"));
        sticker.Should().BeEquivalentTo(new { Path = "out/cat.webm", SizeBytes = 240_000L, HoldsCrf = true });
        sticker.Rung.Crf.Should().Be(40);
        sticker.Concessions.Should().BeEmpty();
    }

    [Fact]
    public async Task Steps_down_until_a_step_fits_and_renders_that_one()
    {
        Estimates(400_000, 300_000, 240_000);

        var sticker = await _service.FitAsync(Fit, Cancel);

        _probes.Select(p => (p.Spec.Fps, p.Crf)).Should().Equal((30, 40), (24, 40), (20, 40));
        _renders.Should().ContainSingle().Which.Spec.Fps.Should().Be(20);
        sticker.Concessions.Should().Equal(new Concession.Fps(30, 20));
        sticker.Steps.Select(s => (s.EstimatedBytes, s.Fits)).Should().Equal((400_000L, false), (300_000L, false), (240_000L, true));
    }

    [Fact]
    public async Task Squeezes_in_the_last_step_when_none_fits_at_its_crf()
    {
        Estimates(400_000);

        var sticker = await _service.FitAsync(Fit, Cancel);

        _probes.Should().HaveCount(7);
        _renders.Should().ContainSingle().Which.Spec.Should().Be(Spec with { Fps = 20, Detail = new PixelSize(384, 384) });
        sticker.HoldsCrf.Should().BeFalse();
        sticker.Rung.Crf.Should().Be(52);
    }

    [Fact]
    public async Task Renders_again_on_a_lower_bitrate_when_the_file_comes_out_over_the_limit()
    {
        Renders(270_000, 250_000);

        var sticker = await _service.FitAsync(Fit, Cancel);

        _renders.Select(r => ((RateControl.TwoPass)r.Rate).BitsPerSecond).Should().Equal(664_098, 600_283);
        sticker.SizeBytes.Should().Be(250_000);
        sticker.Attempts.Should().HaveCount(2);
    }

    [Fact]
    public async Task Gives_a_transparent_sticker_far_under_the_budget_another_render_with_more_bits()
    {
        var fit = Fit with { Spec = Spec with { KeepAlpha = true } };
        Renders(100_000, 220_000);

        var sticker = await _service.FitAsync(fit, Cancel);

        _renders.Select(r => ((RateControl.TwoPass)r.Rate).BitsPerSecond).Should().Equal(332_049, 664_098);
        sticker.SizeBytes.Should().Be(220_000);
    }

    [Fact]
    public async Task Renders_the_last_good_bitrate_again_when_a_fill_and_its_correction_both_overshoot()
    {
        var fit = Fit with { Spec = Spec with { KeepAlpha = true } };
        Renders(100_000, 300_000, 270_000, 100_000);

        var sticker = await _service.FitAsync(fit, Cancel);

        _renders.Should().HaveCount(4);
        _renders[^1].Rate.Should().Be(_renders[0].Rate);
        sticker.SizeBytes.Should().Be(100_000);
    }

    [Fact]
    public async Task Gives_up_with_cannot_fit_when_no_render_comes_under_the_limit()
    {
        Renders(300_000);

        var fit = () => _service.FitAsync(Fit, Cancel);

        (await fit.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.CannotFit);
        _renders.Should().HaveCount(StickerFitService.MaxFinalAttempts);
    }
}
