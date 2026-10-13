using StickerSnap.Core.Projects;
using StickerSnap.Core.Render;
using StickerSnap.Tests.Infrastructure.Projects;

namespace StickerSnap.Tests.Core.Projects;

public sealed class CoverShotTests
{
    private static readonly Project Sample = SampleProjects.Sample;

    private static Project WithSegments(params Segment[] segments) => Sample with { Scene = Scenes.Of(segments) };

    [Fact]
    public void Shows_the_middle_frame_of_the_first_sticker_in_its_crop()
    {
        var cover = CoverShot.Of(Sample);

        // seg-1 plays 0..29, cropped to the centre square of 480×270
        cover.Should().Be(new CoverShot(14, new PixelRect(105, 0, 270, 270), new PixelSize(270, 270)));
    }

    [Fact]
    public void Never_blows_a_small_crop_up_past_its_pixels_and_shrinks_a_big_one_to_the_side()
    {
        var big = Sample with
        {
            Source = Sample.Source with { Media = Scenes.Source with { Width = 1920, Height = 1080 } },
            Scene = Scenes.Of(Scenes.Segment with { Frame = new SegmentFrame.Crop(CropPreset.Free, new FrameRect(0, 0, 1600, 900)) }),
        };

        CoverShot.Of(big).Size.Should().Be(new PixelSize(CoverShot.Side, 216));
    }

    [Fact]
    public void Squeezes_the_whole_picture_for_a_stretch()
    {
        var stretched = WithSegments(Scenes.Segment with
        {
            InFrame = 10,
            OutFrame = 20,
            Frame = new SegmentFrame.Stretch(StretchPreset.Squash, new FrameAspect(1, 1)),
        });

        var cover = CoverShot.Of(stretched);

        cover.Should().Be(new CoverShot(15, new PixelRect(0, 0, 480, 270), new PixelSize(384, 384)));
    }

    [Fact]
    public void Cuts_a_crop_that_runs_past_the_picture_to_it()
    {
        var outside = WithSegments(Scenes.Segment with { Frame = new SegmentFrame.Crop(CropPreset.Free, new FrameRect(400, -20, 200, 100)) });

        CoverShot.Of(outside).Crop.Should().Be(new PixelRect(400, 0, 80, 80));
    }

    [Fact]
    public void Shows_the_middle_of_the_video_for_a_project_without_segments()
    {
        var cover = CoverShot.Of(WithSegments());

        cover.Should().Be(new CoverShot(15, new PixelRect(0, 0, 480, 270), new PixelSize(384, 216)));
    }

    [Fact]
    public void Keeps_the_frame_inside_a_source_shorter_than_the_segment()
    {
        var shorter = Sample with { Source = Sample.Source with { Media = Scenes.Source with { FrameCount = 5 } } };

        CoverShot.Of(shorter).Frame.Should().Be(4);
    }
}
