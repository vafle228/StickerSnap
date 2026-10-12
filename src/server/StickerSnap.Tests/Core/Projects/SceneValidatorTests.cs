using StickerSnap.Core.Errors;
using StickerSnap.Core.Projects;

namespace StickerSnap.Tests.Core.Projects;

public sealed class SceneValidatorTests
{
    private static readonly Segment Segment = Scenes.Segment;

    private static IReadOnlyList<SceneProblem> Validate(Scene scene) => SceneValidator.Validate(scene, Scenes.Source);

    private static IReadOnlyList<SceneProblem> Validate(Segment segment) => Validate(Scenes.Of(segment));

    [Fact]
    public void Finds_nothing_wrong_with_a_valid_scene()
    {
        Validate(Scenes.Valid).Should().BeEmpty();
    }

    [Fact]
    public void Lets_the_last_segment_go()
    {
        Validate(Scenes.Of()).Should().BeEmpty();
    }

    [Fact]
    public void Takes_a_segment_over_the_whole_source_and_a_stretch()
    {
        var whole = Segment with
        {
            InFrame = 0,
            OutFrame = 30,
            Frame = new SegmentFrame.Stretch(StretchPreset.Squash, new FrameAspect(270, 270)),
            Speed = 4,
            Loop = LoopMode.Pingpong,
            Preset = SegmentPreset.Emoji,
        };

        Validate(whole).Should().BeEmpty();
    }

    [Fact]
    public void Lets_an_emoji_keep_a_frame_that_is_not_square()
    {
        // The SPA squeezes any frame of an emoji into 100×100 (frame.ts, outputSize)
        var emoji = Segment with
        {
            Frame = new SegmentFrame.Crop(CropPreset.Source, new FrameRect(0, 0, 480, 270)),
            Preset = SegmentPreset.Emoji,
        };

        Validate(emoji).Should().BeEmpty();
    }

    public static TheoryData<string, Segment, string, string> BrokenSegments => new()
    {
        { "in before the first frame", Segment with { InFrame = -1 }, "segments[0].inFrame", SceneRules.OutOfRange },
        { "out past the last frame", Segment with { OutFrame = 31 }, "segments[0].outFrame", SceneRules.OutOfRange },
        { "out before in", Segment with { InFrame = 10, OutFrame = 9 }, "segments[0].outFrame", SceneRules.BeforeIn },
        { "a speed under 0.25", Segment with { Speed = 0.2 }, "segments[0].speed", SceneRules.OutOfRange },
        { "a speed over 4", Segment with { Speed = 4.01 }, "segments[0].speed", SceneRules.OutOfRange },
        { "a speed that is not a number", Segment with { Speed = double.NaN }, "segments[0].speed", SceneRules.OutOfRange },
        { "an excluded frame before in", Segment with { InFrame = 5, ExcludedFrames = [4] }, "segments[0].excludedFrames[0]", SceneRules.OutOfRange },
        { "an excluded frame after out", Segment with { ExcludedFrames = [3, 30] }, "segments[0].excludedFrames[1]", SceneRules.OutOfRange },
        { "a frame excluded twice", Segment with { ExcludedFrames = [3, 3] }, "segments[0].excludedFrames[1]", SceneRules.Duplicate },
        { "every frame excluded", Segment with { InFrame = 3, OutFrame = 4, ExcludedFrames = [4, 3] }, "segments[0].excludedFrames", SceneRules.NoFramesLeft },
        { "a crop between pixels", Crop(105.5, 0, 270, 270), "segments[0].frame.rect", SceneRules.NotWholePixels },
        { "a crop of a fractional size", Crop(105, 0, 270, 269.5), "segments[0].frame.rect", SceneRules.NotWholePixels },
        { "a crop with no width", Crop(105, 0, 0, 270), "segments[0].frame.rect", SceneRules.Empty },
        { "a crop left of the picture", Crop(-1, 0, 270, 270), "segments[0].frame.rect", SceneRules.OutsideSource },
        { "a crop past the right edge", Crop(211, 0, 270, 270), "segments[0].frame.rect", SceneRules.OutsideSource },
        { "a crop past the bottom edge", Crop(0, 1, 270, 270), "segments[0].frame.rect", SceneRules.OutsideSource },
        { "a stretch to a flat proportion", Stretch(16, 0), "segments[0].frame.aspect", SceneRules.NotPositive },
        { "a stretch to a negative proportion", Stretch(-16, 9), "segments[0].frame.aspect", SceneRules.NotPositive },
        { "a stretch to an infinite proportion", Stretch(double.PositiveInfinity, 9), "segments[0].frame.aspect", SceneRules.NotPositive },
        { "a blank id", Segment with { Id = " " }, "segments[0].id", SceneRules.Empty },
    };

    [Theory]
    [MemberData(nameof(BrokenSegments))]
    public void Names_the_field_of_a_broken_segment(string because, Segment segment, string field, string rule)
    {
        Validate(segment).Should().Equal([new SceneProblem(field, rule)], because);
    }

    [Fact]
    public void Refuses_two_segments_with_one_id()
    {
        Validate(Scenes.Of(Segment, Segment with { Name = "Стикер 2" }))
            .Should().Equal(new SceneProblem("segments[1].id", SceneRules.Duplicate));
    }

    [Fact]
    public void Names_the_segment_by_its_place_in_the_scene()
    {
        Validate(Scenes.Of(Segment, Segment with { Id = "seg-2", Speed = 5 }))
            .Should().Equal(new SceneProblem("segments[1].speed", SceneRules.OutOfRange));
    }

    [Fact]
    public void Refuses_a_scene_schema_it_does_not_know()
    {
        Validate(Scenes.Valid with { SchemaVersion = 2 })
            .Should().Equal(new SceneProblem("schemaVersion", SceneRules.Unsupported));
    }

    [Fact]
    public void Wants_exactly_one_video_layer()
    {
        Validate(Scenes.Valid with { Layers = [] }).Should().Equal(new SceneProblem("layers", SceneRules.OneVideoLayer));
        Validate(Scenes.Valid with { Layers = [new Layer.Video("a"), new Layer.Video("b")] })
            .Should().Equal(new SceneProblem("layers", SceneRules.OneVideoLayer));
    }

    [Fact]
    public void Gives_every_broken_rule_at_once()
    {
        var problems = Validate(Scenes.Of(Segment with { OutFrame = 40, Speed = 0 }));

        problems.Should().Equal(
            new SceneProblem("segments[0].outFrame", SceneRules.OutOfRange),
            new SceneProblem("segments[0].speed", SceneRules.OutOfRange));
    }

    [Fact]
    public void Puts_a_segment_wholly_past_the_end_out_of_range_on_both_sides()
    {
        Validate(Segment with { InFrame = 31, OutFrame = 35 }).Should().Equal(
            new SceneProblem("segments[0].inFrame", SceneRules.OutOfRange),
            new SceneProblem("segments[0].outFrame", SceneRules.OutOfRange));
    }

    [Fact]
    public void Throws_bad_request_with_the_fields_of_the_request_body()
    {
        var save = () => SceneValidator.EnsureValid(Scenes.Of(Segment with { OutFrame = 40, Speed = 0 }), Scenes.Source);

        var error = save.Should().Throw<AppException>().Which;
        error.Code.Should().Be(ErrorCode.BadRequest);
        error.Fields.Should().BeEquivalentTo(new Dictionary<string, string[]>
        {
            ["scene.segments[0].outFrame"] = [SceneRules.OutOfRange],
            ["scene.segments[0].speed"] = [SceneRules.OutOfRange],
        });
    }

    [Fact]
    public void Lets_a_valid_scene_be_saved()
    {
        var save = () => SceneValidator.EnsureValid(Scenes.Valid, Scenes.Source);

        save.Should().NotThrow();
    }

    private static Segment Crop(double x, double y, double width, double height) =>
        Segment with { Frame = new SegmentFrame.Crop(CropPreset.Free, new FrameRect(x, y, width, height)) };

    private static Segment Stretch(double width, double height) =>
        Segment with { Frame = new SegmentFrame.Stretch(StretchPreset.Free, new FrameAspect(width, height)) };
}
