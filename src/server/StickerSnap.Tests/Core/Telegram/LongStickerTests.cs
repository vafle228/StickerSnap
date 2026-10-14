using StickerSnap.Core.Telegram;

namespace StickerSnap.Tests.Core.Telegram;

public sealed class LongStickerTests
{
    private static readonly PresetLimits Sticker = TelegramLimits.Current.Sticker;

    [Theory]
    [InlineData(1.5)]
    [InlineData(3)]
    public void Declares_nothing_of_its_own_for_a_sticker_within_3_seconds(double playSec) =>
        LongSticker.DeclaredFor(playSec, Sticker).Should().BeNull();

    [Theory]
    [InlineData(3.04)]
    [InlineData(10)]
    public void Declares_2_9_seconds_for_a_sticker_that_plays_longer(double playSec) =>
        LongSticker.DeclaredFor(playSec, Sticker).Should().Be(2.9);

    [Fact]
    public void Lets_a_long_sticker_play_up_to_10_seconds()
    {
        Sticker.MaxPlaySec(longSticker: false).Should().Be(3);
        Sticker.MaxPlaySec(longSticker: true).Should().Be(10);
    }
}
