using StickerSnap.Core.Media;
using StickerSnap.Core.Render;
using StickerSnap.Core.Telegram;

namespace StickerSnap.Tests.Core.Render;

public sealed class StickerCheckTests
{
    private static readonly PresetLimits Sticker = TelegramLimits.Current.Sticker;

    [Fact]
    public void Passes_a_3_second_vp9_square_of_512_px_under_256_kb()
    {
        StickerCheck.Problems(Media.Sticker, Sticker).Should().BeEmpty();
    }

    [Fact]
    public void Passes_a_sticker_with_one_side_of_512_px_and_a_shorter_other()
    {
        StickerCheck.Problems(Media.Sticker with { Height = 288 }, Sticker).Should().BeEmpty();
    }

    [Fact]
    public void Allows_a_duration_a_frame_over_3_seconds_as_the_container_rounds_it()
    {
        StickerCheck.Problems(Media.Sticker with { DurationSec = 3.02 }, Sticker).Should().BeEmpty();
    }

    [Fact]
    public void Names_every_rule_a_sticker_breaks()
    {
        var sticker = new MediaInfo(
            MediaFormat.Mp4, "h264", 640, 480, new Rational(60, 1), IsVariableFrameRate: false,
            FrameCount: 240, DurationSec: 4, HasAlpha: false, HasAudio: true, SizeBytes: 300_000);

        StickerCheck.Problems(sticker, Sticker).Should().Equal(
            "300000 B is over 262144 B",
            "h264 in Mp4, not VP9 in WEBM",
            "640x480 has no side of exactly 512 px",
            "4 s is over 3 s",
            "60/1 fps is over 30",
            "has a sound track");
    }
}
