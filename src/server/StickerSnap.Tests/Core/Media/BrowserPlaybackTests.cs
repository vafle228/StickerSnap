using StickerSnap.Core.Media;
using StickerSnap.Tests.Core.Projects;

namespace StickerSnap.Tests.Core.Media;

public sealed class BrowserPlaybackTests
{
    [Theory]
    [InlineData(MediaFormat.Mp4, "h264", true)]
    [InlineData(MediaFormat.Webm, "vp9", true)]
    [InlineData(MediaFormat.Webm, "vp8", true)]
    [InlineData(MediaFormat.Webm, "av1", true)]
    [InlineData(MediaFormat.Mp4, "hevc", false)]
    [InlineData(MediaFormat.Mov, "h264", false)]
    [InlineData(MediaFormat.Gif, "gif", false)]
    [InlineData(MediaFormat.Webp, "webp", false)]
    public void Plays_as_it_is_only_the_video_every_browser_plays(MediaFormat format, string codec, bool asIs)
    {
        var source = Scenes.Source with { Format = format, Codec = codec, Fps = new Rational(30000, 1001) };

        BrowserPlayback.PlaysAsIs(source).Should().Be(asIs);
    }

    [Fact]
    public void Gives_a_variable_rate_a_copy_so_frame_n_sits_at_n_over_fps()
    {
        var phone = Scenes.Source with { Format = MediaFormat.Mp4, Codec = "h264", IsVariableFrameRate = true };

        BrowserPlayback.PlaysAsIs(phone).Should().BeFalse();
    }

    [Fact]
    public void Gives_a_copy_to_a_video_without_a_known_rate()
    {
        var unknown = Scenes.Source with { Format = MediaFormat.Webm, Codec = "vp9", Fps = new Rational(0, 0) };

        BrowserPlayback.PlaysAsIs(unknown).Should().BeFalse();
    }

    [Theory]
    [InlineData(MediaFormat.Mp4, "video/mp4")]
    [InlineData(MediaFormat.Webm, "video/webm")]
    public void Names_the_content_type_of_a_video_played_as_it_is(MediaFormat format, string type)
    {
        BrowserPlayback.ContentTypeOf(format).Should().Be(type);
    }
}
