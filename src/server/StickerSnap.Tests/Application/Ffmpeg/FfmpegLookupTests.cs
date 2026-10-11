using StickerSnap.Application.Ffmpeg;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Ffmpeg;

namespace StickerSnap.Tests.Application.Ffmpeg;

public sealed class FfmpegLookupTests
{
    private const string Directory = "/opt/stickersnap";

    [Fact]
    public void A_found_ffmpeg_of_9_0_or_newer_is_ok()
    {
        var lookup = FfmpegLookup.Found(new FfmpegVersion("9.0.2", 9), Directory);

        lookup.Status.Should().Be(FfmpegStatus.Ok);
        lookup.Require().Should().Be(Directory);
    }

    [Fact]
    public void A_found_ffmpeg_older_than_9_0_is_too_old_and_not_used()
    {
        var lookup = FfmpegLookup.Found(new FfmpegVersion("7.1", 7), Directory);

        lookup.Status.Should().Be(FfmpegStatus.TooOld);
        lookup.Invoking(l => l.Require()).Should().Throw<AppException>()
            .Which.Code.Should().Be(ErrorCode.FfmpegMissing);
    }

    [Fact]
    public void Without_ffmpeg_a_request_that_needs_it_fails_with_ffmpeg_missing()
    {
        FfmpegLookup.Missing.Invoking(l => l.Require()).Should().Throw<AppException>()
            .Which.Code.Should().Be(ErrorCode.FfmpegMissing);
    }
}
