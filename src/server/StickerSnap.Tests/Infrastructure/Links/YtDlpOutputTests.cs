using StickerSnap.Application.Links;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Links;
using StickerSnap.Infrastructure.Links;
using StickerSnap.Tests.TestData;

namespace StickerSnap.Tests.Infrastructure.Links;

/// <summary>
/// yt-dlp's JSON and progress lines. YtDlp/youtube.json is the real `-J` of a YouTube video with
/// the format the adapter picks (VP9 720p), cut to the fields the adapter reads.
/// </summary>
public sealed class YtDlpOutputTests
{
    private static readonly VideoLink Rick = VideoLink.Parse("https://www.youtube.com/watch?v=dQw4w9WgXcQ");

    [Fact]
    public void Reads_the_video_and_the_format_yt_dlp_picked()
    {
        var video = YtDlpOutput.Parse(TestFiles.ReadText("YtDlp/youtube.json"), Rick);

        video.Should().Be(new LinkVideo(
            LinkSite.Youtube,
            "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            "Rick Astley - Never Gonna Give You Up (Official Video) (4K Remaster)",
            213,
            "https://i.ytimg.com/vi_webp/dQw4w9WgXcQ/maxresdefault.webp",
            17_686_717,
            "720p",
            "247",
            "webm"));
    }

    [Fact]
    public void Estimates_the_size_by_the_bitrate_when_the_site_does_not_tell_it()
    {
        var video = YtDlpOutput.Parse(
            """{"title":"t","duration":10,"format_id":"136","ext":"mp4","height":720,"tbr":800}""", Rick);

        video.Bytes.Should().Be(1_000_000);
        video.ThumbnailUrl.Should().BeNull();
    }

    [Theory]
    [InlineData(1280, 720, "720p")]
    [InlineData(720, 1280, "720p")]
    [InlineData(null, 480, "480p")]
    public void Names_the_quality_by_the_shorter_side(int? width, int? height, string quality)
    {
        YtDlpOutput.QualityOf(width, height).Should().Be(quality);
    }

    [Theory]
    [InlineData("is_live")]
    [InlineData("is_upcoming")]
    public void Says_a_stream_cannot_be_downloaded(string state)
    {
        var parse = () => YtDlpOutput.Parse($$"""{"live_status":"{{state}}","format_id":"1","ext":"mp4"}""", Rick);

        parse.Should().Throw<AppException>()
            .Which.Should().Match<AppException>(e => e.Code == ErrorCode.VideoUnavailable && (string)e.Params["reason"] == "live");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"title":"no format"}""")]
    [InlineData("[]")]
    public void Says_the_site_changed_when_the_json_is_no_video(string json)
    {
        var parse = () => YtDlpOutput.Parse(json, Rick);

        parse.Should().Throw<AppException>()
            .Which.Should().Match<AppException>(e => e.Code == ErrorCode.SiteChanged && (string)e.Params["site"] == "youtube");
    }

    [Theory]
    [InlineData("[stickersnap] 1048576 17686717 NA 12", 1_048_576L, 12.0)]
    [InlineData("[stickersnap] 2048.0 NA 17000000.5 NA", 2048L, null)]
    public void Reads_a_progress_line(string line, long received, double? eta)
    {
        YtDlpOutput.Progress(line).Should().Be(new DownloadProgress(received, eta));
    }

    [Theory]
    [InlineData("[youtube] dQw4w9WgXcQ: Downloading webpage")]
    [InlineData("[stickersnap] NA NA NA NA")]
    [InlineData("[stickersnap] 12")]
    public void Skips_any_other_line(string line)
    {
        YtDlpOutput.Progress(line).Should().BeNull();
    }
}
