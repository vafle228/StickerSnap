using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Links;
using StickerSnap.Infrastructure.Ffmpeg;
using StickerSnap.Infrastructure.Links;
using StickerSnap.Infrastructure.Storage;
using StickerSnap.Tests.Infrastructure.Ffmpeg;

namespace StickerSnap.Tests.Infrastructure.Links;

/// <summary>
/// The real YouTube and Giphy, by hand only (`-trait "Category=Online"`): after moving to a newer
/// yt-dlp, or when a site seems to have changed. The videos are old and small ones that are
/// expected to stay: «Me at the zoo», a Giphy gif and a sticker.
/// </summary>
[Trait("Category", Category.Slow)]
[Trait("Category", Category.Online)]
public sealed class OnlineSitesTests(RealYtDlp ytDlp, RealFfmpeg ffmpeg) : IClassFixture<RealYtDlp>, IClassFixture<RealFfmpeg>, IDisposable
{
    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("stickersnap-online-");

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public void Dispose() => _data.Delete(recursive: true);

    private GiphySite Giphy()
    {
        var clients = new ServiceCollection().AddHttpClient().BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        return new GiphySite(
            clients, ffmpeg.Probe, Options.Create(new DataOptions { DataDir = _data.FullName }),
            new FakeTimeProvider(DateTimeOffset.UtcNow), NullLogger<GiphySite>.Instance);
    }

    [Fact]
    public async Task Probes_and_downloads_a_youtube_video()
    {
        var video = await ytDlp.Site.ProbeAsync(VideoLink.Parse("https://youtu.be/jNQXAC9IVRw?t=3"), Cancel);

        video.Title.Should().Be("Me at the zoo");
        video.DurationSec.Should().Be(19);
        video.ThumbnailUrl.Should().StartWith("https://");
        video.Bytes.Should().BePositive();

        var path = Path.Combine(_data.FullName, $"zoo.{video.Extension}");
        var bytes = await ytDlp.Site.DownloadAsync(video, path, _ => { }, Cancel);
        bytes.Should().BeCloseTo(video.Bytes, (ulong)(video.Bytes / 10));
        (await ffmpeg.Probe.ProbeAsync(path, Cancel)).DurationSec.Should().BeApproximately(19, 1);
    }

    [Fact]
    public async Task Says_a_youtube_video_that_does_not_exist_is_gone()
    {
        var probe = () => ytDlp.Site.ProbeAsync(VideoLink.Parse("https://www.youtube.com/watch?v=aaaaaaaaaaa"), Cancel);

        (await probe.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.VideoUnavailable);
    }

    [Theory]
    [InlineData("https://giphy.com/gifs/cat-typing-JIX9t2j0ZTN9S", "MP4")]
    [InlineData("https://giphy.com/stickers/3oKIPjzfv0sI2p7fDW", "WEBP")]
    public async Task Probes_and_downloads_a_giphy_gif_and_sticker(string url, string quality)
    {
        var site = Giphy();

        var video = await site.ProbeAsync(VideoLink.Parse(url), Cancel);
        var path = Path.Combine(_data.FullName, $"giphy.{video.Extension}");
        var bytes = await site.DownloadAsync(video, path, _ => { }, Cancel);

        video.Quality.Should().Be(quality);
        video.DurationSec.Should().BePositive();
        bytes.Should().Be(video.Bytes);
    }
}
