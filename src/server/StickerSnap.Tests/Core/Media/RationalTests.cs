using StickerSnap.Core.Media;

namespace StickerSnap.Tests.Core.Media;

public sealed class RationalTests
{
    [Theory]
    [InlineData("30000/1001", 30000, 1001)]
    [InlineData("2:1", 2, 1)]
    [InlineData("0/0", 0, 0)]
    public void Reads_a_rate_and_an_aspect_ratio_as_ffprobe_writes_them(string text, int num, int den)
    {
        Rational.Parse(text).Should().Be(new Rational(num, den));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("N/A")]
    [InlineData("30")]
    public void Is_null_for_anything_else(string? text)
    {
        Rational.Parse(text).Should().BeNull();
    }

    [Fact]
    public void Is_valid_only_when_both_sides_are_positive()
    {
        new Rational(30, 1).IsValid.Should().BeTrue();
        new Rational(0, 0).IsValid.Should().BeFalse();
        new Rational(30, 0).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Converts_without_losing_ntsc_rates()
    {
        new Rational(30000, 1001).ToDouble().Should().BeApproximately(29.97, 0.001);
        new Rational(30000, 1001).ToString().Should().Be("30000/1001");
    }
}
