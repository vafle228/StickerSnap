using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using StickerSnap.Application.Account;
using StickerSnap.Application.Diagnostics;
using StickerSnap.Application.Jobs;
using StickerSnap.Application.Packs;
using StickerSnap.Application.Projects;
using StickerSnap.Application.Settings;
using StickerSnap.Core.Account;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Jobs;
using StickerSnap.Core.Media;
using StickerSnap.Core.Packs;
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
    private readonly ControlledExecutor _publishes = new(JobKind.Publish);
    private readonly InMemoryProjectRegistry _registry = new();
    private readonly IProjectStore _store = Substitute.For<IProjectStore>();
    private readonly JobQueue _queue;
    private readonly JobService _service;
    private readonly IPackService _packs = Substitute.For<IPackService>();
    private readonly IAccountService _account = Substitute.For<IAccountService>();
    private Project _onDisk = Cat;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public JobServiceTests()
    {
        _store.ReadAsync(CatPath, Arg.Any<CancellationToken>()).Returns(_ => _onDisk);
        var sources = Substitute.For<ISourcePresence>();
        sources.Of(Arg.Any<string>()).Returns(PresenceStatus.Ok);
        var projects = new ProjectService(
            _registry, _store, Substitute.For<ITrash>(), sources, _events, Imports.Unused(), Substitute.For<ISettingsService>(),
            Substitute.For<ISourceWatch>(), _clock);
        _queue = new JobQueue([_exports, _publishes], _events, Substitute.For<IErrorJournal>(), _clock, NullLogger<JobQueue>.Instance);
        _service = new JobService(_queue, projects, _packs, _account);
        _account.GetAsync(Arg.Any<CancellationToken>()).Returns(new AccountLink.Linked(new LinkedUser(42, "Дима", "dima"), "DESKTOP-7F3K2Q"));
        HasPacks(Cats(count: 3));
    }

    public ValueTask DisposeAsync() => _queue.DisposeAsync();

    private const string CatsName = "cats_by_StickerSnapAppBot";

    private static Pack Cats(int count, PackKind kind = PackKind.Regular, PresenceStatus status = PresenceStatus.Ok) =>
        new(CatsName, "Коты", kind, status, null,
            [.. Enumerable.Range(0, count).Select(i => new PackSticker($"U{i}", $"F{i}", ["🐱"], null))]);

    private void HasPacks(params Pack[] packs) =>
        _packs.ListAsync(Arg.Any<CancellationToken>()).Returns(packs);

    private static JobRequest.Publish Publish(PublishTarget target, params PublishItem[] items) =>
        new(Cat.Id, 12, items.Length > 0 ? items : [new PublishItem("seg-1", ["🐱"], null)], target);

    private static readonly PublishTarget IntoCats = new PublishTarget.Existing(CatsName);

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

    [Fact]
    public async Task Queues_a_publication_into_a_pack_of_the_registry_naming_the_pack()
    {
        await ListedAsync();

        var job = await _service.CreateAsync(Publish(IntoCats), Cancel);

        job.Kind.Should().Be(JobKind.Publish);
        job.Pack.Should().Be(CatsName);
    }

    [Fact]
    public async Task Names_a_new_pack_with_the_bots_tail()
    {
        await ListedAsync();

        var job = await _service.CreateAsync(Publish(new PublishTarget.New("Кот", "kot_divan", PackKind.Regular)), Cancel);

        job.Pack.Should().Be("kot_divan_by_StickerSnapAppBot");
    }

    [Fact]
    public async Task Refuses_a_publication_without_a_linked_account()
    {
        await ListedAsync();
        _account.GetAsync(Arg.Any<CancellationToken>()).Returns(new AccountLink.Unlinked());

        (await RefusedAsync(Publish(IntoCats))).Code.Should().Be(ErrorCode.AccountNotLinked);
    }

    [Fact]
    public async Task Refuses_a_pack_that_is_full_unknown_or_gone_from_telegram()
    {
        await ListedAsync();

        HasPacks(Cats(count: 120));
        var full = await RefusedAsync(Publish(IntoCats));
        HasPacks(Cats(count: 3, status: PresenceStatus.Missing));
        var gone = await RefusedAsync(Publish(IntoCats));
        HasPacks();
        var unknown = await RefusedAsync(Publish(IntoCats));

        full.Code.Should().Be(ErrorCode.PackFull);
        gone.Code.Should().Be(ErrorCode.PackNotFound);
        unknown.Code.Should().Be(ErrorCode.PackNotFound);
    }

    [Fact]
    public async Task Names_the_items_telegram_would_refuse()
    {
        await ListedAsync();

        var refusal = await RefusedAsync(Publish(
            IntoCats,
            new PublishItem("seg-1", [], new StickerRef(CatsName, "U9")),
            new PublishItem("seg-2", ["🐱"], null)));

        refusal.Code.Should().Be(ErrorCode.BadRequest);
        refusal.Fields.Should().BeEquivalentTo(new Dictionary<string, string[]>
        {
            ["items[0].emoji"] = ["count"],
            ["items[0].replace"] = ["not_in_pack"],
            ["items[1]"] = ["wrong_type"],
        });
    }

    [Fact]
    public async Task Refuses_a_new_pack_name_telegram_would_refuse()
    {
        await ListedAsync();

        var refusal = await RefusedAsync(Publish(new PublishTarget.New("Кот", "1kot", PackKind.Regular)));

        refusal.Code.Should().Be(ErrorCode.PackNameInvalid);
        refusal.Fields.Should().ContainKey("target.name").WhoseValue.Should().Equal("bad_start");
    }

    [Fact]
    public async Task Counts_only_the_stickers_not_sent_yet_against_the_room_of_a_retry()
    {
        await ListedAsync();
        _onDisk = Cat with
        {
            Scene = Cat.Scene with { Segments = [Cat.Scene.Segments[0], Cat.Scene.Segments[1] with { Preset = SegmentPreset.Sticker }] },
        };
        HasPacks(Cats(count: 118));
        var job = await _service.CreateAsync(
            Publish(IntoCats, new PublishItem("seg-1", ["🐱"], null), new PublishItem("seg-2", ["😺"], null)), Cancel);
        var call = await _publishes.NextAsync();
        call.Run.Done("seg-1");
        call.Throw(new AppException(ErrorCode.NetworkUnavailable, "offline"));
        await _events.UntilAsync(job.Id, JobState.Partial);
        HasPacks(Cats(count: 119));

        var retried = await _service.RetryAsync(job.Id, Cancel);

        retried.State.Should().Be(JobState.Running);
    }
}
