using StickerSnap.Core.Media;
using StickerSnap.Core.Render;
using StickerSnap.Core.Telegram;

namespace StickerSnap.Tests.Core.Render;

public sealed class StickerSpecTests
{
    private static readonly PresetLimits Sticker = TelegramLimits.Current.Sticker;

    [Fact]
    public void Takes_the_first_3_seconds_of_the_middle_square_at_512_px_and_30_fps()
    {
        var spec = StickerSpec.Default(Media.Landscape, Sticker);

        spec.Should().Be(new StickerSpec(
            StartSec: 0, DurationSec: 3, new PixelRect(420, 0, 1080, 1080), Width: 512, Height: 512, Fps: 30, KeepAlpha: false));
    }

    [Fact]
    public void Centres_the_square_of_a_portrait_picture_vertically()
    {
        var spec = StickerSpec.Default(Media.Landscape with { Width = 720, Height = 1280 }, Sticker);

        spec.Crop.Should().Be(new PixelRect(0, 280, 720, 720));
    }

    [Fact]
    public void Keeps_the_side_of_the_square_even_for_an_odd_picture()
    {
        var spec = StickerSpec.Default(Media.Landscape with { Width = 405, Height = 721 }, Sticker);

        spec.Crop.Should().Be(new PixelRect(0, 158, 404, 404));
    }

    [Fact]
    public void Keeps_the_whole_of_a_source_shorter_than_3_seconds()
    {
        StickerSpec.Default(Media.Landscape with { DurationSec = 1.5 }, Sticker).DurationSec.Should().Be(1.5);
    }

    [Fact]
    public void Keeps_the_alpha_of_a_transparent_source()
    {
        StickerSpec.Default(Media.Landscape with { HasAlpha = true }, Sticker).KeepAlpha.Should().BeTrue();
    }

    [Fact]
    public void Scales_to_the_100_px_of_a_custom_emoji()
    {
        var spec = StickerSpec.Default(Media.Landscape, TelegramLimits.Current.Emoji);

        (spec.Width, spec.Height).Should().Be((100, 100));
    }

    [Fact]
    public void Refuses_a_picture_too_small_to_crop()
    {
        var spec = () => StickerSpec.Default(Media.Landscape with { Width = 1, Height = 1 }, Sticker);

        spec.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(10, 1, false, 10)]
    [InlineData(4, 1, false, 4)]
    [InlineData(30000, 1001, false, 30)]
    [InlineData(60, 1, false, 30)]
    [InlineData(25, 2, false, 30)]
    [InlineData(10, 1, true, 30)]
    [InlineData(0, 0, false, 30)]
    public void Plays_at_the_rate_of_a_steady_whole_numbered_source_under_30_fps_and_at_30_otherwise(
        int num, int den, bool variable, int fps)
    {
        var source = Media.Landscape with { Fps = new Rational(num, den), IsVariableFrameRate = variable };

        StickerSpec.Default(source, Sticker).Fps.Should().Be(fps);
    }

    [Fact]
    public void Takes_the_frame_rate_and_the_detail_of_a_step_of_the_fit_ladder()
    {
        var spec = StickerSpec.Default(Media.Landscape, Sticker);
        var rungs = FitLadder.Rungs(spec, LadderProfile.Sticker);

        spec.At(rungs[0]).Should().Be(spec);
        spec.At(rungs[^1]).Should().Be(spec with { Fps = 20, Detail = new PixelSize(384, 384) });
    }
}
