using StickerSnap.Core.Render;

namespace StickerSnap.Tests.Core.Render;

public sealed class TargetBitrateTests
{
    [Fact]
    public void Fills_95_percent_of_256_kb_over_3_seconds_with_about_664_kbit_per_second()
    {
        TargetBitrate.BitsPerSecond(262_144, 3).Should().Be(664_098);
    }

    [Fact]
    public void Gives_a_shorter_sticker_more_bits_per_second()
    {
        TargetBitrate.BitsPerSecond(262_144, 1.5).Should().Be(1_328_196);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void Refuses_a_duration_that_is_not_positive(double durationSec)
    {
        var bitrate = () => TargetBitrate.BitsPerSecond(262_144, durationSec);

        bitrate.Should().Throw<ArgumentOutOfRangeException>();
    }
}
