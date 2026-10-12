using StickerSnap.Core.Media;

namespace StickerSnap.Tests.Core.Media;

public sealed class SourceInfoTests
{
    [Fact]
    public void Shows_what_ffprobe_read_with_the_name_and_path_of_the_file()
    {
        var media = new MediaInfo(
            MediaFormat.Webm, "vp9", 512, 512, new Rational(30, 1), IsVariableFrameRate: false,
            FrameCount: 90, DurationSec: 3, HasAlpha: true, HasAudio: false, SizeBytes: 120_000);

        var source = SourceInfo.From(media, "cat.webm", "C:/clips/cat.webm");

        source.Should().Be(new SourceInfo(
            "cat.webm", "C:/clips/cat.webm", PresenceStatus.Ok, MediaFormat.Webm, "vp9", 512, 512,
            new Rational(30, 1), 90, 3, HasAlpha: true, 120_000));
    }

    [Theory]
    [InlineData("mp4", true)]
    [InlineData("webp", true)]
    [InlineData("avi", false)]
    [InlineData("", false)]
    public void Lets_through_the_extensions_of_the_five_formats(string extension, bool supported)
    {
        MediaFormats.IsSupportedExtension(extension).Should().Be(supported);
    }
}
