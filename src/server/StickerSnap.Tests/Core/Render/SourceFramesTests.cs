using StickerSnap.Core.Render;

namespace StickerSnap.Tests.Core.Render;

public sealed class SourceFramesTests
{
    [Fact]
    public void Counts_the_frames_from_first_to_last_less_the_excluded()
    {
        new SourceFrames(10, 19, [12, 15]).Count.Should().Be(8);
    }

    [Fact]
    public void Splits_the_frames_into_runs_around_the_excluded_ones()
    {
        new SourceFrames(0, 9, [0, 3, 4, 7, 9]).Runs().Should().Equal(new FrameRun(1, 2), new FrameRun(5, 6), new FrameRun(8, 8));
        new SourceFrames(5, 9, []).Runs().Should().Equal(new FrameRun(5, 9));
    }

    [Fact]
    public void Takes_the_first_frames_that_play_skipping_the_excluded_ones()
    {
        new SourceFrames(0, 9, [3, 4, 8]).Take(5).Should().Be(new SourceFrames(0, 6, [3, 4]));
        new SourceFrames(0, 9, [3]).Take(3).Should().Be(new SourceFrames(0, 2, []));
        new SourceFrames(0, 9, [3]).Take(20).Should().Be(new SourceFrames(0, 9, [3]));
    }

    [Fact]
    public void Is_equal_to_the_same_frames_in_another_list()
    {
        var frames = new SourceFrames(0, 9, new List<int> { 3 });

        frames.Should().Be(new SourceFrames(0, 9, [3]));
        frames.Should().NotBe(new SourceFrames(0, 9, [4]));
    }
}
