using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using StickerSnap.Application.Diagnostics;
using StickerSnap.Application.Links;
using StickerSnap.Application.Projects;
using StickerSnap.Application.Sources;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Links;
using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;
using StickerSnap.Tests.Infrastructure.Projects;

namespace StickerSnap.Tests.Application.Links;

/// <summary>
/// The downloads with sites the test drives, uploads and projects substituted and a fake clock:
/// one at a time, the room checked first, cancelling, failures, and how downloadUpdated is thinned out.
/// </summary>
public sealed class DownloadServiceTests : IAsyncDisposable
{
    private const string Cat = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";
    private const string Dog = "https://www.youtube.com/watch?v=jNQXAC9IVRw";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
    private readonly ControlledSites _sites = new();
    private readonly IUploadStore _uploads = Substitute.For<IUploadStore>();
    private readonly IProjectService _projects = Substitute.For<IProjectService>();
    private readonly DownloadEvents _events = new();
    private readonly ErrorJournal _journal = new(NullLogger<ErrorJournal>.Instance);
    private readonly DownloadService _service;
    private readonly TaskCompletionSource<string> _discarded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _slots;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public DownloadServiceTests()
    {
        _uploads.FreeBytes().Returns(10_000_000_000);
        _uploads.Reserve(Arg.Any<string>()).Returns(call => new UploadSlot($"up-{++_slots}", $"/uploads/up-{_slots}/{call.Arg<string>()}"));
        _projects.CreateAsync(Arg.Any<SourceRef>(), Arg.Any<CancellationToken>())
            .Returns(new OpenedProject(SampleProjects.Sample, "/projects/cat.ssproj", PresenceStatus.Ok));
        _uploads.When(uploads => uploads.Discard(Arg.Any<string>())).Do(call => _discarded.TrySetResult(call.Arg<string>()));
        _service = new DownloadService(
            new LinkService(_sites, _clock), _sites, _uploads, _projects, _events, _journal, _clock,
            NullLogger<DownloadService>.Instance);
    }

    public ValueTask DisposeAsync() => _service.DisposeAsync();

    [Fact]
    public async Task Downloads_into_an_upload_named_after_the_video_and_makes_a_project_of_it()
    {
        var download = await _service.CreateAsync("youtu.be/dQw4w9WgXcQ", Cancel);
        var call = await _sites.NextAsync();

        call.Complete(37_500_000);
        var done = await _events.UntilAsync(download.Id, DownloadState.Succeeded);

        download.State.Should().Be(DownloadState.Downloading);
        call.Path.Should().Be("/uploads/up-1/Кот смотрит в окно.webm");
        await _projects.Received(1).CreateAsync(new SourceRef.Upload("up-1"), Arg.Any<CancellationToken>());
        done.Should().Match<Download>(d => d.ProjectId == SampleProjects.Sample.Id && d.ReceivedBytes == 37_500_000);
        _uploads.DidNotReceive().Discard(Arg.Any<string>());
        _service.List().Should().Equal(done);
    }

    [Fact]
    public async Task Runs_one_download_at_a_time()
    {
        var first = await _service.CreateAsync(Cat, Cancel);
        var call = await _sites.NextAsync();
        var second = await _service.CreateAsync(Dog, Cancel);

        second.State.Should().Be(DownloadState.Queued);
        _service.List().Select(d => d.Id).Should().Equal(second.Id, first.Id);

        call.Complete(10);
        (await _sites.NextAsync()).Video.Url.Should().Be(Dog);
        await _events.UntilAsync(first.Id, DownloadState.Succeeded);
    }

    [Fact]
    public async Task Cancelling_stops_the_download_and_removes_its_file()
    {
        var download = await _service.CreateAsync(Cat, Cancel);
        var call = await _sites.NextAsync();

        var canceled = _service.Cancel(download.Id);

        canceled.State.Should().Be(DownloadState.Canceled);
        call.Token.IsCancellationRequested.Should().BeTrue();
        await _events.UntilAsync(download.Id, DownloadState.Canceled);
        (await Discarded()).Should().Be("up-1");
        _service.Cancel(download.Id).Should().Be(canceled, "a finished download is answered as it is");
    }

    [Fact]
    public async Task Cancelling_a_waiting_download_takes_it_out_of_the_queue()
    {
        await _service.CreateAsync(Cat, Cancel);
        var call = await _sites.NextAsync();
        var waiting = await _service.CreateAsync(Dog, Cancel);

        _service.Cancel(waiting.Id).State.Should().Be(DownloadState.Canceled);
        call.Complete(10);
        await _service.DisposeAsync();

        _sites.HasNext.Should().BeFalse();
    }

    [Fact]
    public async Task Fails_with_disk_full_before_a_byte_comes_when_the_video_does_not_fit()
    {
        _uploads.FreeBytes().Returns(12_000_000);

        var download = await _service.CreateAsync(Cat, Cancel);
        var failed = await _events.UntilAsync(download.Id, DownloadState.Failed);

        failed.Error!.Code.Should().Be(ErrorCode.DiskFull);
        failed.Error.Params.Should().BeEquivalentTo(new Dictionary<string, object> { ["needBytes"] = 38_000_000L, ["freeBytes"] = 12_000_000L });
        _uploads.DidNotReceive().Reserve(Arg.Any<string>());
    }

    [Fact]
    public async Task Fills_in_the_room_when_the_disk_fills_up_on_the_way()
    {
        var download = await _service.CreateAsync(Cat, Cancel);
        var call = await _sites.NextAsync();
        _uploads.FreeBytes().Returns(1000);

        call.Throw(new AppException(ErrorCode.DiskFull, "No space left"));
        var failed = await _events.UntilAsync(download.Id, DownloadState.Failed);

        failed.Error!.Params.Should().BeEquivalentTo(new Dictionary<string, object> { ["needBytes"] = 38_000_000L, ["freeBytes"] = 1000L });
    }

    [Fact]
    public async Task Fails_with_what_the_site_said_and_keeps_it_for_a_report()
    {
        var download = await _service.CreateAsync(Cat, Cancel);
        var call = await _sites.NextAsync();

        call.Throw(new AppException(ErrorCode.SiteChanged, "yt-dlp failed") { Log = "ERROR: Unable to extract" });
        var failed = await _events.UntilAsync(download.Id, DownloadState.Failed);

        failed.Error!.Should().Match<StickerSnap.Core.Jobs.JobError>(e => e.Code == ErrorCode.SiteChanged && e.Log == "ERROR: Unable to extract");
        (await Discarded()).Should().Be("up-1");
        _journal.Find(download.Id).Should().ContainSingle().Which.Operation.Should().Be("download");
    }

    [Fact]
    public async Task Sends_its_progress_at_most_ten_times_a_second_with_the_latest_bytes()
    {
        var download = await _service.CreateAsync(Cat, Cancel);
        var call = await _sites.NextAsync();
        _clock.Advance(DownloadService.EventInterval);
        var before = _events.Sent(download.Id).Count;

        call.Progress(new DownloadProgress(1000, 30));
        call.Progress(new DownloadProgress(2000, 20));
        call.Progress(new DownloadProgress(3000, 10));
        _events.Sent(download.Id).Should().HaveCount(before);

        _clock.Advance(DownloadService.EventInterval);
        _events.Sent(download.Id).Should().HaveCount(before + 1);
        _events.Sent(download.Id)[^1].Should().Match<Download>(d => d.ReceivedBytes == 3000 && d.EtaSec == 10);
    }

    [Fact]
    public async Task Keeps_a_finished_download_for_ten_minutes()
    {
        var download = await _service.CreateAsync(Cat, Cancel);
        (await _sites.NextAsync()).Complete(10);
        await _events.UntilAsync(download.Id, DownloadState.Succeeded);

        _clock.Advance(DownloadService.KeepFinished);

        _service.List().Should().BeEmpty();
        _service.Invoking(s => s.Cancel(download.Id)).Should().Throw<AppException>()
            .Which.Code.Should().Be(ErrorCode.JobNotFound);
    }

    [Fact]
    public async Task Answers_a_bad_link_at_once_without_queueing_it()
    {
        var create = () => _service.CreateAsync("https://www.youtube.com/playlist?list=PLx", Cancel);

        (await create.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.VideoUnavailable);
        _service.List().Should().BeEmpty();
    }

    [Theory]
    [InlineData("Кот: смотрит / в окно?", "webm", "Кот_ смотрит   в окно_.webm")]
    [InlineData("...", "mp4", "video.mp4")]
    public void Names_the_file_after_the_title(string title, string extension, string name)
    {
        var video = Videos.Of(Cat) with { Title = title, Extension = extension };

        DownloadService.FileNameOf(video).Should().Be(name);
    }

    [Fact]
    public void Cuts_a_long_title_for_the_file()
    {
        var video = Videos.Of(Cat) with { Title = new string('к', 200) };

        DownloadService.FileNameOf(video).Should().Be(new string('к', DownloadService.NameLength) + ".webm");
    }

    /// <summary>The upload removed: the download's own thread does it after its last event.</summary>
    private Task<string> Discarded() => _discarded.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancel);
}
