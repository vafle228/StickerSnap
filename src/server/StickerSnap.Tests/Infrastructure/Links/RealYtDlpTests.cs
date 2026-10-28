using StickerSnap.Application.Links;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Links;

namespace StickerSnap.Tests.Infrastructure.Links;

/// <summary>
/// The adapter over the real yt-dlp, on a clip served from 127.0.0.1: the probe's JSON, the
/// download with its progress lines, stopping it half way, and the errors of a missing file and
/// of no server at all.
/// </summary>
[Trait("Category", Category.Slow)]
public sealed class RealYtDlpTests(RealYtDlp ytDlp) : IClassFixture<RealYtDlp>
{
    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private VideoLink Link(string file) => new(LinkSite.Youtube, file, ytDlp.BaseUrl + file);

    [Fact]
    public async Task Probes_a_video_and_the_file_it_would_download()
    {
        var video = await ytDlp.Site.ProbeAsync(Link("clip.webm"), Cancel);

        video.Should().Match<LinkVideo>(v => v.Site == LinkSite.Youtube && v.Url == ytDlp.BaseUrl + "clip.webm");
        video.Title.Should().Be("clip");
        video.Extension.Should().Be("webm");
        video.Variant.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Downloads_the_video_telling_how_far_it_got()
    {
        var video = await ytDlp.Site.ProbeAsync(Link("clip.webm"), Cancel);
        var path = ytDlp.Clip($"{Guid.NewGuid():N} 100% кот.webm");
        var steps = new List<DownloadProgress>();

        var bytes = await ytDlp.Site.DownloadAsync(video, path, step => { lock (steps) steps.Add(step); }, Cancel);

        bytes.Should().Be(new FileInfo(ytDlp.ClipPath).Length);
        (await File.ReadAllBytesAsync(path, Cancel)).Should().Equal(await File.ReadAllBytesAsync(ytDlp.ClipPath, Cancel));
        steps.Should().NotBeEmpty();
        steps[^1].ReceivedBytes.Should().Be(bytes);
    }

    [Fact]
    public async Task Stops_a_download_half_way_when_cancelled()
    {
        var video = await ytDlp.Site.ProbeAsync(Link("slow.webm"), Cancel);
        var path = ytDlp.Clip($"{Guid.NewGuid():N}.webm");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Cancel);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var download = ytDlp.Site.DownloadAsync(video, path, _ => started.TrySetResult(), stop.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancel);
        await stop.CancelAsync();

        await download.Invoking(async d => await d).Should().ThrowAsync<OperationCanceledException>();
        File.Exists(path).Should().BeFalse("only a .part file was being written");
    }

    [Fact]
    public async Task Says_a_video_that_is_not_there_is_gone()
    {
        var probe = () => ytDlp.Site.ProbeAsync(Link("missing.webm"), Cancel);

        (await probe.Should().ThrowAsync<AppException>())
            .Which.Should().Match<AppException>(e => e.Code == ErrorCode.VideoUnavailable && (string)e.Params["reason"] == "gone");
    }

    [Fact]
    public async Task Says_the_site_did_not_answer_when_nothing_listens()
    {
        var link = new VideoLink(LinkSite.Youtube, "clip", "http://127.0.0.1:9/clip.webm");

        var probe = () => ytDlp.Site.ProbeAsync(link, Cancel);

        (await probe.Should().ThrowAsync<AppException>())
            .Which.Should().Match<AppException>(e => e.Code == ErrorCode.SiteUnreachable && (string)e.Params["site"] == "youtube");
    }
}
