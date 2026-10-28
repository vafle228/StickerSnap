using StickerSnap.Core.Errors;
using StickerSnap.Core.Jobs;
using StickerSnap.Core.Links;

namespace StickerSnap.Tests.Core.Links;

/// <summary>The moves of a download: queued → downloading → reading → succeeded, failed or canceled on the way.</summary>
public sealed class DownloadTests
{
    public static readonly LinkVideo Video = new(
        LinkSite.Youtube, "https://www.youtube.com/watch?v=dQw4w9WgXcQ", "Кот смотрит в окно", 48,
        "https://i.ytimg.com/vi/dQw4w9WgXcQ/hqdefault.jpg", 38_000_000, "720p", "247", "webm");

    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Goes_from_the_queue_to_a_project()
    {
        var project = Guid.NewGuid();

        var download = Download.Queue("d1", Video, Now).Start().Receive(1000, 12).Receive(5000, 3).Read(38_000_000).Succeed(project);

        download.Should().Be(new Download("d1", Video, DownloadState.Succeeded, 38_000_000, Now, null, project));
        download.IsOver.Should().BeTrue();
    }

    [Fact]
    public void Never_counts_the_bytes_back()
    {
        var download = Download.Queue("d1", Video, Now).Start().Receive(5000, 3).Receive(4000, 2);

        download.ReceivedBytes.Should().Be(5000);
        download.EtaSec.Should().Be(2);
    }

    [Fact]
    public void Fails_or_is_canceled_on_any_step_before_its_end()
    {
        var error = JobError.Of(ErrorCode.SiteChanged, "yt-dlp failed");
        var reading = Download.Queue("d1", Video, Now).Start().Read(10);

        reading.Fail(error).Should().Match<Download>(d => d.State == DownloadState.Failed && d.Error == error);
        Download.Queue("d1", Video, Now).Cancel().State.Should().Be(DownloadState.Canceled);
    }

    [Fact]
    public void Does_not_move_once_it_ended()
    {
        var canceled = Download.Queue("d1", Video, Now).Start().Cancel();

        canceled.Invoking(d => d.Read(10)).Should().Throw<InvalidOperationException>();
        canceled.Invoking(d => d.Cancel()).Should().Throw<InvalidOperationException>();
        Download.Queue("d1", Video, Now).Invoking(d => d.Receive(1, null)).Should().Throw<InvalidOperationException>();
    }
}
