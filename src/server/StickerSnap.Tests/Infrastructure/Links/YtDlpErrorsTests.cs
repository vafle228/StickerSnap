using StickerSnap.Core.Errors;
using StickerSnap.Core.Links;
using StickerSnap.Infrastructure.Links;

namespace StickerSnap.Tests.Infrastructure.Links;

/// <summary>yt-dlp's `ERROR:` lines as the contract's codes, with the log kept for «Подробности».</summary>
public sealed class YtDlpErrorsTests
{
    private const string Url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";

    private const string Warning =
        "WARNING: [youtube] No supported JavaScript runtime could be found.";

    private static AppException Of(string error) =>
        YtDlpErrors.Of($"{Warning}\n{error}", LinkSite.Youtube, Url, "yt-dlp -J …");

    [Theory]
    [InlineData("ERROR: [youtube] aaaaaaaaaaa: Video unavailable. This video is no longer available", "gone")]
    [InlineData("ERROR: [youtube] dQw4w9WgXcQ: Private video. Sign in if you've been granted access", "gone")]
    [InlineData("ERROR: [youtube] dQw4w9WgXcQ: Sign in to confirm your age. This video may be inappropriate for some users.", "adult")]
    [InlineData("ERROR: [youtube] dQw4w9WgXcQ: This live event will begin in 3 hours.", "live")]
    public void Says_why_the_video_cannot_be_downloaded(string error, string reason)
    {
        var exception = Of(error);

        exception.Code.Should().Be(ErrorCode.VideoUnavailable);
        exception.Params["reason"].Should().Be(reason);
        exception.Log.Should().Be($"{Warning}\n{error}");
        exception.Context["command"].Should().Be("yt-dlp -J …");
    }

    [Theory]
    [InlineData("ERROR: [youtube] dQw4w9WgXcQ: Unable to download API page: <urlopen error [Errno 11001] getaddrinfo failed>")]
    [InlineData("ERROR: [youtube] dQw4w9WgXcQ: Unable to download webpage: The read operation timed out")]
    [InlineData("ERROR: unable to download video data: <urlopen error [Errno 111] Connection refused>")]
    public void Says_the_site_did_not_answer(string error)
    {
        var exception = Of(error);

        exception.Code.Should().Be(ErrorCode.SiteUnreachable);
        exception.Params["site"].Should().Be("youtube");
    }

    [Fact]
    public void Says_the_disk_is_full()
    {
        Of("ERROR: unable to write data: [Errno 28] No space left on device").Code.Should().Be(ErrorCode.DiskFull);
    }

    [Fact]
    public void Says_a_page_yt_dlp_does_not_know_is_another_site()
    {
        var exception = Of("ERROR: Unsupported URL: https://www.youtube.com/feed/library");

        exception.Code.Should().Be(ErrorCode.LinkUnsupported);
        exception.Params["reason"].Should().Be("site");
    }

    [Fact]
    public void Says_the_site_changed_for_anything_else()
    {
        var exception = Of("ERROR: [youtube] dQw4w9WgXcQ: Unable to extract player response; please report this issue");

        exception.Code.Should().Be(ErrorCode.SiteChanged);
        exception.Params["site"].Should().Be("youtube");
        exception.Message.Should().Contain("Unable to extract player response");
    }
}
