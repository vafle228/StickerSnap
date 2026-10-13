using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Render;
using StickerSnap.Core.Telegram;
using StickerSnap.Tests.Core.Projects;

namespace StickerSnap.Tests.Core.Render;

public sealed class SizeForecastTests
{
    private static readonly PresetLimits Sticker = TelegramLimits.Current.Sticker;
    private static readonly LadderProfile Profile = LadderProfile.Sticker;

    /// <summary>The fit ladder's budget for a sticker: 95% of 256 KB.</summary>
    private const long Budget = 249_036;

    /// <summary>1920×1080 at 30 fps, 150 frames.</summary>
    private static readonly MediaInfo Video = Media.Landscape;

    /// <summary>A GIF of 31 frames at 10 fps, like the sample project's.</summary>
    private static readonly MediaInfo Gif = Media.Landscape with
    {
        Format = MediaFormat.Gif, Codec = "gif", Width = 480, Height = 270, Fps = new Rational(10, 1), FrameCount = 31, DurationSec = 3.1,
    };

    /// <summary>Two seconds of the video: 60 frames played.</summary>
    private static readonly Segment TwoSeconds = Scenes.Segment with
    {
        OutFrame = 59,
        Frame = new SegmentFrame.Crop(CropPreset.Square, new FrameRect(420, 0, 1080, 1080)),
    };

    private static IReadOnlyList<SizeSuggestion> Suggest(Segment segment, long bytes, MediaInfo? source = null) =>
        SizeForecast.Suggestions(segment, source ?? Video, Sticker, Profile, bytes, Budget);

    [Fact]
    public void Allows_the_ladder_s_budget_with_fitting_on_and_the_limit_with_it_off()
    {
        SizeForecast.Allowance(Sticker, autoFit: true).Should().Be(Budget);
        SizeForecast.Allowance(Sticker, autoFit: false).Should().Be(262_144);
    }

    [Fact]
    public void Shortens_to_the_most_frames_the_bytes_per_frame_allow()
    {
        // 400 000 B over 60 frames: 6 667 B a frame, so 37 frames fit in 249 036 B
        var suggestions = Suggest(TwoSeconds, 400_000);

        suggestions[0].Should().Be(new SizeSuggestion.Trim(36, 37 / 30.0));
    }

    [Fact]
    public void Speeds_up_to_the_slowest_speed_that_fits()
    {
        // 60 frames at 1.6× are 38 (37.5 rounded up), at 1.65× 36
        var suggestions = Suggest(TwoSeconds, 400_000);

        suggestions[1].Should().Be(new SizeSuggestion.Speed(1.65));
    }

    [Fact]
    public void Counts_the_frames_of_a_ping_pong_both_ways()
    {
        // 30 frames forward play 58; at 10 000 B a frame 24 fit, which 13 frames forward make
        var pingPong = TwoSeconds with { OutFrame = 29, Loop = LoopMode.Pingpong };

        var trim = Suggest(pingPong, 580_000).OfType<SizeSuggestion.Trim>().Single();

        trim.Should().Be(new SizeSuggestion.Trim(12, 24 / 30.0));
    }

    [Fact]
    public void Skips_the_excluded_frames_when_it_shortens()
    {
        // 58 frames play; 24 fit: frames 0 to 24 less frame 5
        var cut = TwoSeconds with { ExcludedFrames = [5, 50] };

        var trim = Suggest(cut, 580_000).OfType<SizeSuggestion.Trim>().Single();

        trim.OutFrame.Should().Be(24);
    }

    [Fact]
    public void Offers_no_speed_where_a_faster_low_rate_source_plays_the_same_frames()
    {
        // 30 frames of a 10 fps GIF play as 30 frames up to 3×, and as 23 at 4×; 18 fit
        var gif = Scenes.Segment;

        var suggestions = Suggest(gif, 400_000, Gif);

        suggestions.Should().Equal(new SizeSuggestion.Trim(17, 1.8));
    }

    [Fact]
    public void Speeds_up_a_segment_cut_at_3_s_only_once_it_plays_whole_in_fewer_frames()
    {
        // 150 frames are cut to 90; 56 fit at 4 444 B a frame, which 150 frames make from 2.7×
        var long5s = TwoSeconds with { OutFrame = 149 };

        var speed = Suggest(long5s, 400_000).OfType<SizeSuggestion.Speed>().Single();

        speed.Should().Be(new SizeSuggestion.Speed(2.7));
    }

    [Fact]
    public void Offers_nothing_when_even_one_frame_is_over()
    {
        Suggest(TwoSeconds, 60 * 300_000L).Should().BeEmpty();
    }

    [Theory]
    [InlineData(260_000)]
    [InlineData(300_000)]
    [InlineData(450_000)]
    [InlineData(900_000)]
    [InlineData(4_000_000)]
    public void Each_suggestion_fits_by_the_model_and_gives_up_no_more_than_it_must(long bytes)
    {
        var perFrame = (double)bytes / SizeForecast.PlayedFrames(TwoSeconds, Video, Sticker, Profile);
        double Weight(Segment segment) => perFrame * SizeForecast.PlayedFrames(segment, Video, Sticker, Profile);

        foreach (var suggestion in Suggest(TwoSeconds, bytes))
        {
            switch (suggestion)
            {
                case SizeSuggestion.Trim trim:
                    Weight(TwoSeconds with { OutFrame = trim.OutFrame }).Should().BeLessThanOrEqualTo(Budget);
                    Weight(TwoSeconds with { OutFrame = trim.OutFrame + 1 }).Should().BeGreaterThan(Budget);
                    break;
                case SizeSuggestion.Speed speed:
                    Weight(TwoSeconds with { Speed = speed.Value }).Should().BeLessThanOrEqualTo(Budget);
                    Weight(TwoSeconds with { Speed = speed.Value - SizeForecast.SpeedStep }).Should().BeGreaterThan(Budget);
                    break;
            }
        }
    }

    [Fact]
    public void Shortens_more_and_speeds_up_more_the_heavier_the_sticker()
    {
        var lighter = Suggest(TwoSeconds, 300_000);
        var heavier = Suggest(TwoSeconds, 450_000);

        lighter.OfType<SizeSuggestion.Trim>().Single().OutFrame
            .Should().BeGreaterThan(heavier.OfType<SizeSuggestion.Trim>().Single().OutFrame);
        lighter.OfType<SizeSuggestion.Speed>().Single().Value
            .Should().BeLessThan(heavier.OfType<SizeSuggestion.Speed>().Single().Value);
    }
}
