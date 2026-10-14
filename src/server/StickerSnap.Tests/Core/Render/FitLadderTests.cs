using StickerSnap.Core.Projects;
using StickerSnap.Core.Render;
using StickerSnap.Core.Telegram;
using StickerSnap.Tests.Core.Projects;

namespace StickerSnap.Tests.Core.Render;

public sealed class FitLadderTests
{
    private static readonly PresetLimits Sticker = TelegramLimits.Current.Sticker;
    private static readonly StickerSpec Spec = StickerSpec.Default(Media.Landscape, Sticker);
    private static readonly PixelSize Full = new(512, 512);

    [Fact]
    public void Steps_down_the_frame_rate_then_raises_the_crf_then_gives_up_detail()
    {
        var rungs = FitLadder.Rungs(Spec, LadderProfile.Sticker);

        rungs.Select(r => (r.Fps, r.Crf, r.Detail)).Should().Equal(
            (30, 40, Full),
            (24, 40, Full),
            (20, 40, Full),
            (20, 46, Full),
            (20, 52, Full),
            (20, 52, new PixelSize(448, 448)),
            (20, 52, new PixelSize(384, 384)));
    }

    [Fact]
    public void Names_what_each_step_gives_up_against_the_first()
    {
        var rungs = FitLadder.Rungs(Spec, LadderProfile.Sticker);

        rungs[0].Concessions.Should().BeEmpty();
        rungs[2].Concessions.Should().Equal(new Concession.Fps(30, 20));
        rungs[^1].Concessions.Should().Equal(new Concession.Fps(30, 20), new Concession.Crf(40, 52), new Concession.Scale(512, 384));
    }

    [Fact]
    public void Gives_up_more_on_each_step_and_never_takes_back_what_an_earlier_one_gave()
    {
        var rungs = FitLadder.Rungs(Spec, LadderProfile.Sticker);

        rungs.Zip(rungs.Skip(1)).Should().AllSatisfy(pair =>
        {
            var (before, after) = pair;
            after.Fps.Should().BeLessThanOrEqualTo(before.Fps);
            after.Crf.Should().BeGreaterThanOrEqualTo(before.Crf);
            after.Detail.LongerSide.Should().BeLessThanOrEqualTo(before.Detail.LongerSide);
            (after.Fps, after.Crf, after.Detail).Should().NotBe((before.Fps, before.Crf, before.Detail));
        });
    }

    [Fact]
    public void Skips_the_frame_rates_a_10_fps_sticker_never_had()
    {
        var rungs = FitLadder.Rungs(Spec with { Fps = 10 }, LadderProfile.Sticker);

        rungs.Should().AllSatisfy(r => r.Fps.Should().Be(10));
        rungs.SelectMany(r => r.Concessions).Should().NotContain(c => c is Concession.Fps);
    }

    [Fact]
    public void Takes_a_long_sticker_down_to_12_fps_before_it_raises_the_crf()
    {
        var rungs = FitLadder.Rungs(Spec, LadderProfile.Long);

        rungs.Select(r => (r.Fps, r.Crf)).Should().StartWith([(30, 40), (24, 40), (20, 40), (15, 40), (12, 40), (12, 46)]);
    }

    [Theory]
    [InlineData(89, false, false)]
    [InlineData(149, false, false)]
    [InlineData(89, true, false)]
    [InlineData(149, true, true)]
    public void Gives_the_long_ladder_only_to_a_sticker_that_plays_past_3_seconds(int outFrame, bool longSticker, bool climbsLong)
    {
        var segment = Scenes.Segment with
        {
            InFrame = 0,
            OutFrame = outFrame,
            LongSticker = longSticker,
            Frame = new SegmentFrame.Crop(CropPreset.Square, new FrameRect(420, 0, 1080, 1080)),
        };
        var spec = SceneCompiler.Compile(segment, Media.Landscape, Sticker);

        LadderProfile.For(spec, Sticker).Should().BeSameAs(climbsLong ? LadderProfile.Long : LadderProfile.Sticker);
    }

    [Fact]
    public void Starts_at_the_top_rate_of_a_profile_that_wants_fewer_frames()
    {
        var longSticker = new LadderProfile([15, 12, 10], [31, 37], []);

        var rungs = FitLadder.Rungs(Spec, longSticker);

        rungs.Select(r => (r.Fps, r.Crf)).Should().Equal((15, 31), (12, 31), (10, 31), (10, 37));
        rungs[0].Concessions.Should().BeEmpty();
    }

    [Theory]
    [InlineData(512, 512)]
    [InlineData(512, 288)]
    [InlineData(288, 512)]
    [InlineData(100, 100)]
    public void Keeps_the_size_of_the_sticker_and_gives_up_detail_in_even_pixels(int width, int height)
    {
        var spec = Spec with { Width = width, Height = height };

        var rungs = FitLadder.Rungs(spec, LadderProfile.Sticker);

        rungs.Should().AllSatisfy(r =>
        {
            spec.At(r).Size.Should().Be(new PixelSize(width, height));
            (r.Detail.Width % 2, r.Detail.Height % 2).Should().Be((0, 0));
            r.Detail.Width.Should().BeLessThanOrEqualTo(width);
            r.Detail.Height.Should().BeLessThanOrEqualTo(height);
        });
    }

    [Fact]
    public void Shrinks_a_landscape_sticker_and_a_custom_emoji_by_an_eighth_and_a_quarter()
    {
        FitLadder.Rungs(Spec with { Width = 512, Height = 288 }, LadderProfile.Sticker).TakeLast(2).Select(r => r.Detail)
            .Should().Equal(new PixelSize(448, 252), new PixelSize(384, 216));
        FitLadder.Rungs(Spec with { Width = 100, Height = 100 }, LadderProfile.Sticker).TakeLast(2).Select(r => r.Detail)
            .Should().Equal(new PixelSize(88, 88), new PixelSize(76, 76));
    }

    [Fact]
    public void Fits_a_step_estimated_within_95_percent_of_256_kb()
    {
        FitLadder.Budget(Sticker).Should().Be(249_036);
        FitLadder.Fits(249_036, Sticker).Should().BeTrue();
        FitLadder.Fits(249_037, Sticker).Should().BeFalse();
    }

    [Fact]
    public void Lowers_the_bitrate_of_a_render_over_the_limit_in_proportion_and_a_little_more()
    {
        var next = FitLadder.Retarget(new RateControl.TwoPass(3_984_588), 275_967, Sticker, transparent: false);

        next.Should().Be(new RateControl.TwoPass(3_523_826));
    }

    [Theory]
    [InlineData(false, 100_000)]
    [InlineData(false, 262_144)]
    [InlineData(true, 190_000)]
    [InlineData(true, 262_144)]
    public void Keeps_a_render_under_the_limit_unless_it_is_transparent_and_far_under(bool transparent, long bytes)
    {
        FitLadder.Retarget(new RateControl.TwoPass(332_049), bytes, Sticker, transparent).Should().BeNull();
    }

    [Fact]
    public void Gives_the_bits_a_transparent_sticker_left_unused_to_its_picture()
    {
        var next = FitLadder.Retarget(new RateControl.TwoPass(332_049), 131_005, Sticker, transparent: true);

        next.Should().Be(new RateControl.TwoPass(568_092));
    }

    [Fact]
    public void Gives_each_stream_of_a_transparent_sticker_at_most_the_bitrate_of_an_opaque_one()
    {
        var next = FitLadder.Retarget(new RateControl.TwoPass(332_049), 38_000, Sticker, transparent: true);

        next.Should().Be(new RateControl.TwoPass(664_098));
    }
}
