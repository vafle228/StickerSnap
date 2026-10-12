using StickerSnap.Core.Render;

namespace StickerSnap.Tests.Core.Render;

public sealed class PlayLengthTests
{
    [Fact]
    public void Plays_the_frames_of_a_plain_loop_once()
    {
        PlayLength.Frames(90, pingPong: false).Should().Be(90);
        PlayLength.Seconds(90, 30, pingPong: false).Should().Be(3);
    }

    [Fact]
    public void Plays_a_ping_pong_forward_and_back_without_repeating_either_end()
    {
        PlayLength.Frames(46, pingPong: true).Should().Be(90);
        PlayLength.Seconds(46, 30, pingPong: true).Should().Be(3);
    }

    [Fact]
    public void Plays_a_single_frame_once_in_ping_pong_too()
    {
        PlayLength.Frames(1, pingPong: true).Should().Be(1);
    }

    [Theory]
    [InlineData(3, 30, false, 90)]
    [InlineData(3, 30, true, 46)]
    [InlineData(3, 20, true, 31)]
    [InlineData(3, 10, false, 30)]
    public void Lets_a_segment_run_forward_only_as_long_as_its_loop_plays_within_the_limit(double maxSec, int fps, bool pingPong, int frames)
    {
        var forward = PlayLength.MaxForwardFrames(maxSec, fps, pingPong);

        forward.Should().Be(frames);
        PlayLength.Seconds(forward, fps, pingPong).Should().BeLessThanOrEqualTo(maxSec);
        PlayLength.Seconds(forward + 1, fps, pingPong).Should().BeGreaterThan(maxSec);
    }
}
