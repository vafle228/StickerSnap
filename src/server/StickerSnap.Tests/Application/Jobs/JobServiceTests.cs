using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using StickerSnap.Application.Jobs;
using StickerSnap.Application.Projects;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Jobs;
using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;
using StickerSnap.Tests.Application.Projects;
using StickerSnap.Tests.Core.Projects;
using StickerSnap.Tests.Infrastructure.Projects;

namespace StickerSnap.Tests.Application.Jobs;

/// <summary>What POST /jobs and «Повторить» check against the project before a job is queued.</summary>
public sealed class JobServiceTests : IAsyncDisposable
{
    private static readonly Project Cat = SampleProjects.Sample;
    private static readonly string CatPath = OperatingSystem.IsWindows() ? @"C:\Users\u\cat.ssproj" : "/home/u/cat.ssproj";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));
    private readonly RecordingPublisher _events = new();
    private readonly ControlledExecutor _exports = new(JobKind.Export);
    private readonly InMemoryProjectRegistry _registry = new();
    private readonly IProjectStore _store = Substitute.For<IProjectStore>();
    private readonly JobQueue _queue;
    private readonly JobService _service;
    private Project _onDisk = Cat;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public JobServiceTests()
    {
        _store.ReadAsync(CatPath, Arg.Any<CancellationToken>()).Returns(_ => _onDisk);
        var sources = Substitute.For<ISourcePresence>();
        sources.Of(Arg.Any<string>()).Returns(PresenceStatus.Ok);
        var projects = new ProjectService(_registry, _store, Substitute.For<ITrash>(), sources, _events, _clock);
        _queue = new JobQueue([_exports], _events, _clock, NullLogger<JobQueue>.Instance);
        _service = new JobService(_queue, projects);
    }

    public ValueTask DisposeAsync() => _queue.DisposeAsync();

    private async Task ListedAsync() => await _registry.SaveAsync(ProjectCard.Of(Cat, CatPath), Cancel);

    private async Task<AppException> RefusedAsync(JobRequest request)
    {
        var create = () => _service.CreateAsync(request, Cancel);
        var refusal = (await create.Should().ThrowAsync<AppException>()).Which;
        _queue.List().Should().BeEmpty("a refused request queues nothing");
        return refusal;
    }

    [Fact]
    public async Task Queues_an_export_of_saved_segments_at_the_saved_revision()
    {
        await ListedAsync();

        var job = await _service.CreateAsync(new JobRequest.Export(Cat.Id, 12, ["seg-2", "seg-1"]), Cancel);

        job.Kind.Should().Be(JobKind.Export);
        job.ProjectId.Should().Be(Cat.Id);
        job.Items.Select(item => item.SegmentId).Should().Equal("seg-2", "seg-1");
        _service.Get(job.Id).Id.Should().Be(job.Id);
    }

    [Fact]
    public async Task Refuses_a_project_that_is_not_on_the_list()
    {
        var refusal = await RefusedAsync(new JobRequest.Export(Guid.NewGuid(), 12, ["seg-1"]));

        refusal.Code.Should().Be(ErrorCode.ProjectNotFound);
    }

    [Fact]
    public async Task Refuses_an_export_of_a_revision_that_is_not_the_saved_one()
    {
        await ListedAsync();

        var refusal = await RefusedAsync(new JobRequest.Export(Cat.Id, 11, ["seg-1"]));

        refusal.Code.Should().Be(ErrorCode.RevisionConflict);
    }

    [Fact]
    public async Task Refuses_segments_the_project_does_not_have_once_each()
    {
        await ListedAsync();

        var refusal = await RefusedAsync(new JobRequest.Export(Cat.Id, 12, ["seg-1", "seg-9", "seg-1"]));

        refusal.Code.Should().Be(ErrorCode.BadRequest);
        refusal.Fields.Should().BeEquivalentTo(new Dictionary<string, string[]>
        {
            ["segmentIds[1]"] = ["unknown"],
            ["segmentIds[2]"] = ["duplicate"],
        });
    }

    [Fact]
    public async Task Refuses_an_export_of_no_segment()
    {
        await ListedAsync();

        var refusal = await RefusedAsync(new JobRequest.Export(Cat.Id, 12, []));

        refusal.Fields.Should().ContainKey("segmentIds").WhoseValue.Should().Equal("empty");
    }

    [Fact]
    public async Task Refuses_a_publication_of_a_stale_revision_or_of_unknown_segments()
    {
        await ListedAsync();
        var target = new PublishTarget.Existing("cats_by_StickerSnapAppBot");

        var stale = await RefusedAsync(new JobRequest.Publish(Cat.Id, 13, [new PublishItem("seg-1", ["🐱"], null)], target));
        var unknown = await RefusedAsync(new JobRequest.Publish(Cat.Id, 12, [new PublishItem("seg-9", ["🐱"], null)], target));

        stale.Code.Should().Be(ErrorCode.RevisionConflict);
        unknown.Fields.Should().ContainKey("items[0]").WhoseValue.Should().Equal("unknown");
    }

    [Fact]
    public async Task Queues_a_preview_of_a_segment_not_saved_yet()
    {
        await ListedAsync();
        var unsaved = Scenes.Segment with { Id = "seg-new", InFrame = 5, OutFrame = 15 };

        var job = await _service.CreateAsync(new JobRequest.Preview(Cat.Id, unsaved), Cancel);

        job.Items.Single().SegmentId.Should().Be("seg-new");
    }

    [Fact]
    public async Task Refuses_a_preview_of_a_segment_that_breaks_a_rule_naming_its_field()
    {
        await ListedAsync();
        var outside = Scenes.Segment with { OutFrame = 40 };

        var refusal = await RefusedAsync(new JobRequest.Preview(Cat.Id, outside));

        refusal.Code.Should().Be(ErrorCode.BadRequest);
        refusal.Fields.Keys.Should().ContainSingle().Which.Should().StartWith("segment.");
    }

    [Fact]
    public async Task Checks_the_revision_again_before_a_retry()
    {
        await ListedAsync();
        var job = await _service.CreateAsync(new JobRequest.Export(Cat.Id, 12, ["seg-1"]), Cancel);
        (await _exports.NextAsync()).Throw(new AppException(ErrorCode.DiskFull, "No space left on device"));
        await _events.UntilAsync(job.Id, JobState.Failed);
        _onDisk = Cat with { Revision = 13 };

        var retry = () => _service.RetryAsync(job.Id, Cancel);

        (await retry.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.RevisionConflict);
        _service.Get(job.Id).State.Should().Be(JobState.Failed);
    }

    [Fact]
    public async Task Retries_a_failed_job_while_the_project_is_as_it_was()
    {
        await ListedAsync();
        var job = await _service.CreateAsync(new JobRequest.Export(Cat.Id, 12, ["seg-1"]), Cancel);
        (await _exports.NextAsync()).Throw(new AppException(ErrorCode.DiskFull, "No space left on device"));
        await _events.UntilAsync(job.Id, JobState.Failed);

        var retried = await _service.RetryAsync(job.Id, Cancel);

        retried.State.Should().Be(JobState.Running);
        (await _exports.NextAsync()).Run.Job.Id.Should().Be(job.Id);
    }
}
