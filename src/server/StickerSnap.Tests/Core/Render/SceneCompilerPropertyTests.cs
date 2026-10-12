using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Render;

namespace StickerSnap.Tests.Core.Render;

/// <summary>
/// What holds for every valid segment of every source, checked on thousands of random ones (the
/// seed is fixed, so a failure repeats): at every step of the fit ladder the sticker has a side of
/// exactly the preset's and even sides no longer than it, and plays within Telegram's 3 s.
/// </summary>
public sealed class SceneCompilerPropertyTests
{
    private const int Cases = 5000;

    public static TheoryData<int> Seeds => [1, 2, 3];

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Every_valid_segment_makes_a_sticker_with_one_exact_side_and_even_sides(int seed)
    {
        var random = new Random(seed);
        for (var i = 0; i < Cases; i++)
        {
            var (source, segment) = Draw(random);
            var limits = SceneCompiler.LimitsOf(segment.Preset);
            var spec = SceneCompiler.Compile(SceneOf(segment), segment.Id, source);

            foreach (var rung in FitLadder.Rungs(spec, LadderProfile.Sticker))
            {
                var step = spec.At(rung);
                var because = $"{segment} of {source} at {rung}";
                step.Size.LongerSide.Should().Be(limits.Side, because);
                (step.Width % 2, step.Height % 2).Should().Be((0, 0), because);
                Math.Min(step.Width, step.Height).Should().BePositive(because);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Every_valid_segment_plays_within_3_seconds_at_every_rate_of_the_ladder(int seed)
    {
        var random = new Random(seed);
        for (var i = 0; i < Cases; i++)
        {
            var (source, segment) = Draw(random);
            var limits = SceneCompiler.LimitsOf(segment.Preset);
            var spec = SceneCompiler.Compile(SceneOf(segment), segment.Id, source);

            spec.Clip.Frames.First.Should().Be(segment.InFrame);
            spec.Clip.Frames.Last.Should().BeInRange(segment.InFrame, segment.OutFrame);
            spec.Fps.Should().BeInRange(1, limits.MaxFps);
            foreach (var rung in FitLadder.Rungs(spec, LadderProfile.Sticker))
            {
                var step = spec.At(rung);
                var because = $"{segment} of {source} at {rung}";
                step.DurationSec.Should().BeLessThanOrEqualTo(limits.MaxDurationSec + 1e-9, because);
                step.PlayedFrames.Should().BePositive(because);
            }
        }
    }

    private static Scene SceneOf(Segment segment) => new(Scene.CurrentSchemaVersion, [new Layer.Video("video")], [segment]);

    /// <summary>A source of any size, rate and length, and a segment the validator lets through.</summary>
    private static (MediaInfo Source, Segment Segment) Draw(Random random)
    {
        Rational[] rates = [new(30, 1), new(30000, 1001), new(24000, 1001), new(25, 1), new(60, 1), new(10, 1), new(12, 1), new(15, 1), new(7, 1), new(0, 0)];
        var width = random.Next(2, 4000);
        var height = random.Next(2, 4000);
        var frames = random.Next(1, 2000);
        var fps = rates[random.Next(rates.Length)];
        var duration = frames / (fps.IsValid ? fps.ToDouble() : 25);
        var source = new MediaInfo(
            MediaFormat.Mp4, "h264", width, height, fps, random.Next(4) == 0, frames, duration,
            HasAlpha: random.Next(3) == 0, HasAudio: false, SizeBytes: 1);

        var inFrame = random.Next(frames);
        var outFrame = random.Next(inFrame, frames);
        var excluded = Enumerable.Range(inFrame, outFrame - inFrame + 1).Where(_ => random.Next(10) == 0).Take(outFrame - inFrame).ToList();

        SegmentFrame frame;
        if (random.Next(2) == 0)
        {
            var w = random.Next(1, width + 1);
            var h = random.Next(1, height + 1);
            frame = new SegmentFrame.Crop(CropPreset.Free, new FrameRect(random.Next(width - w + 1), random.Next(height - h + 1), w, h));
        }
        else
        {
            frame = new SegmentFrame.Stretch(StretchPreset.Free, new FrameAspect(random.NextDouble() * 100 + 0.01, random.NextDouble() * 100 + 0.01));
        }

        var segment = new Segment(
            "seg", "Стикер", inFrame, outFrame, excluded, frame,
            Speed: Math.Round(0.25 + random.NextDouble() * 3.75, 2),
            random.Next(2) == 0 ? LoopMode.Normal : LoopMode.Pingpong,
            random.Next(4) == 0 ? SegmentPreset.Emoji : SegmentPreset.Sticker,
            LongSticker: false, AutoFit: true);
        return (source, segment);
    }
}
