using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;
using StickerSnap.Tests.Core.Projects;

namespace StickerSnap.Tests.Core.Media;

public sealed class FrameStripTests
{
    private static readonly MediaInfo Video = Scenes.Source with { Format = MediaFormat.Mp4, Codec = "h264", FrameCount = 300 };

    [Fact]
    public void Lays_the_frames_out_in_one_row_as_wide_as_the_proportion_gives()
    {
        var strip = FrameStrip.Of(Video, from: 30, count: 30, height: 96);

        // 480×270 at 96 px tall is 170.67 px wide: a whole pixel per frame
        strip.FrameWidth.Should().Be(171);
        strip.Width.Should().Be(171 * 30);
        strip.Last.Should().Be(59);
    }

    [Fact]
    public void Gives_a_tall_picture_a_narrow_frame_of_at_least_a_pixel()
    {
        var tall = Video with { Width = 1, Height = 1000 };

        FrameStrip.Of(tall, 0, 10, 96).FrameWidth.Should().Be(1);
    }

    [Fact]
    public void Takes_the_last_frames_up_to_the_end_of_the_source()
    {
        var strip = FrameStrip.Of(Video, from: 270, count: 30, height: 48);

        strip.Last.Should().Be(299);
    }

    [Theory]
    [InlineData(-1, 30, 96, "from")]
    [InlineData(300, 1, 96, "from")]
    [InlineData(280, 30, 96, "count")]
    [InlineData(0, 0, 96, "count")]
    [InlineData(0, FrameStrip.MaxCount + 1, 10, "count")]
    [InlineData(0, 30, 0, "height")]
    [InlineData(0, 30, FrameStrip.MaxHeight + 1, "height")]
    public void Refuses_frames_past_the_source_and_sizes_past_the_limits(int from, int count, int height, string field)
    {
        var refusal = () => FrameStrip.Of(Video, from, count, height);

        var error = refusal.Should().Throw<AppException>().Which;
        error.Code.Should().Be(ErrorCode.BadRequest);
        error.Fields.Should().ContainKey(field).WhoseValue.Should().Equal("out_of_range");
    }

    [Fact]
    public void Refuses_a_strip_wider_than_a_webp_picture_can_be()
    {
        // 120 frames of a 16:9 source 720 px tall would be 153 600 px wide
        var refusal = () => FrameStrip.Of(Video, 0, FrameStrip.MaxCount, FrameStrip.MaxHeight);

        refusal.Should().Throw<AppException>().Which.Fields.Should().ContainKey("count").WhoseValue.Should().Equal("too_wide");
    }

    [Fact]
    public void Takes_a_strip_right_at_the_width_limit()
    {
        // A square source 136 px tall: 120 × 136 = 16 320 px
        var square = Video with { Width = 500, Height = 500 };

        FrameStrip.Of(square, 0, FrameStrip.MaxCount, 136).Width.Should().BeLessThanOrEqualTo(FrameStrip.MaxWidth);
    }
}
