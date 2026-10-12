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
}
