using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using StickerSnap.Application.Account;
using StickerSnap.Application.Events;
using StickerSnap.Application.Exports;
using StickerSnap.Application.Jobs;
using StickerSnap.Application.Media;
using StickerSnap.Application.Packs;
using StickerSnap.Application.ProjectMedia;
using StickerSnap.Application.Projects;
using StickerSnap.Application.Publish;
using StickerSnap.Application.Render;
using StickerSnap.Application.Settings;
using StickerSnap.Core.Account;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Jobs;
using StickerSnap.Core.Media;
using StickerSnap.Core.Packs;
using StickerSnap.Core.Projects;
using StickerSnap.Core.Settings;
using StickerSnap.Core.Validation;
using StickerSnap.Tests.Application.Exports;
using StickerSnap.Tests.Application.Jobs;
using StickerSnap.Tests.Application.Packs;
using StickerSnap.Tests.Core.Projects;
using StickerSnap.Tests.Infrastructure.Projects;

namespace StickerSnap.Tests.Application.Publish;

/// <summary>
/// «Опубликовать» run by the queue against a scripted Telegram: three stickers into a new pack,
/// a 429 waited out, a refused sticker and «Повторить», a cancel on the way, a file that fails
/// the checks, a replacement in place, and what the registry and «Недавние» keep.
/// </summary>
public sealed class PublishExecutorTests : IAsyncDisposable
{
    private const string Cats = "cats_by_StickerSnapAppBot";
    private const long Owner = 42;

    private static readonly Project Cat = SampleProjects.Sample with
    {
        Scene = SampleProjects.Sample.Scene with
        {
            Segments =
            [
                SampleProjects.Sample.Scene.Segments[0],
                SampleProjects.Sample.Scene.Segments[1] with { Preset = SegmentPreset.Sticker },
                SampleProjects.Sample.Scene.Segments[0] with { Id = "seg-3", Name = "Стикер 3" },
            ],
        },
    };

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeTelegram _telegram = new();
    private readonly InMemoryPackRepository _registry = new();
    private readonly IProjectService _projects = Substitute.For<IProjectService>();
    private readonly IAccountService _account = Substitute.For<IAccountService>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly ISegmentExporter _exporter = Substitute.For<ISegmentExporter>();
    private readonly IMediaProbe _probe = Substitute.For<IMediaProbe>();
    private readonly ISourcePresence _presence = Substitute.For<ISourcePresence>();
    private readonly RecordingPublisher _events = new();
    private readonly List<string> _exported = [];
    private readonly JobQueue _queue;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public PublishExecutorTests()
    {
        Opens(Cat);
        _account.GetAsync(Arg.Any<CancellationToken>()).Returns(new AccountLink.Linked(new LinkedUser(Owner, "Дима", "dima"), "DESKTOP-7F3K2Q"));
        _settings.GetAsync(Arg.Any<CancellationToken>()).Returns(UserSettings.Default(new Folders("/projects", "/stickers")));
        _presence.Of(Arg.Any<string>()).Returns(PresenceStatus.Missing);
        ExportsAs(_ => ValidationVerdict.Pass);

        var packs = new PackService(
            _registry, _telegram, Substitute.For<IProjectRegistry>(), _presence, Substitute.For<IMediaCache>(),
            Substitute.For<IEventPublisher>(), _clock);
        var executor = new PublishExecutor(
            _projects, _account, packs, _telegram, _settings, _exporter, _probe, _presence, _clock);
        _queue = new JobQueue([executor], _events, _clock, NullLogger<JobQueue>.Instance);
    }

    public ValueTask DisposeAsync() => _queue.DisposeAsync();

    private void Opens(Project project) =>
        _projects.GetAsync(project.Id, Arg.Any<CancellationToken>())
            .Returns(new OpenedProject(project, "/projects/cat.ssproj", PresenceStatus.Ok));

    /// <summary>The exporter makes «Стикер N.webm», checked with the verdict <paramref name="verdict"/> gives the segment.</summary>
    private void ExportsAs(Func<string, ValidationVerdict> verdict) =>
        _exporter.ExportAsync(Arg.Any<Project>(), Arg.Any<Segment>(), Arg.Any<string>(), Arg.Any<IProgress<RenderProgress>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var segment = call.Arg<Segment>();
                lock (_exported) _exported.Add(segment.Id);
                Renders.ReportStages(call.Arg<IProgress<RenderProgress>>());
                var export = SampleExports.Of(segment, Cat.Source);
                return export with { Validation = export.Validation with { Verdict = verdict(segment.Id) } };
            });

    private static JobRequest.Publish Request(PublishTarget target, params PublishItem[] items) =>
        new(Cat.Id, 12, items.Length > 0 ? items : Three, target);

    private static readonly PublishItem[] Three =
    [
        new("seg-1", ["🐱"], null),
        new("seg-2", ["😂", "🔥"], null),
        new("seg-3", ["😴"], null),
    ];

    private static readonly PublishTarget NewCats = new PublishTarget.New("Коты", "cats", PackKind.Regular);

    private Job Start(JobRequest.Publish request) => _queue.Enqueue(request);

    private Task<Job> UntilAsync(Job job, JobState state) => _events.UntilAsync(job.Id, state);

    /// <summary>
    /// The job once it waits for Telegram. Read from the queue, not from jobUpdated: the queue holds
    /// back events within 100 ms of each other, and this clock does not move by itself.
    /// </summary>
    private async Task<Job> WaitingAsync(Job job)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (_queue.Get(job.Id) is { Progress.Stage: JobStage.Wait } waiting) return waiting;
            await Task.Delay(5, Cancel);
        }
        throw new TimeoutException($"Job {job.Id} did not start to wait");
    }

    private IReadOnlyList<string> Sends =>
        [.. _telegram.Calls.Where(call => call.StartsWith("create") || call.StartsWith("add") || call.StartsWith("replace"))];

    [Fact]
    public async Task Makes_a_new_pack_with_the_first_sticker_and_adds_the_others()
    {
        var done = await UntilAsync(Start(Request(NewCats)), JobState.Succeeded);

        Sends.Should().Equal($"create {Cats}", $"add {Cats}", $"add {Cats}");
        done.Pack.Should().Be(Cats);
        done.Items.Select(item => item.Sticker).Should().Equal(
            new StickerRef(Cats, "U1"), new StickerRef(Cats, "U2"), new StickerRef(Cats, "U3"));
        done.Items.Should().OnlyContain(item => item.State == JobItemState.Done && item.Export != null);
        _exported.Should().Equal("seg-1", "seg-2", "seg-3");
    }

    [Fact]
    public async Task Keeps_each_sticker_in_the_registry_with_its_project_and_all_its_emoji()
    {
        await UntilAsync(Start(Request(NewCats)), JobState.Succeeded);

        var pack = _registry.Packs.Should().ContainSingle().Which;
        pack.Name.Should().Be(Cats);
        pack.Stickers.Select(sticker => (sticker.Id, string.Join(" ", sticker.Emoji), sticker.Origin?.SegmentId)).Should().Equal(
            ("U1", "🐱", "seg-1"), ("U2", "😂 🔥", "seg-2"), ("U3", "😴", "seg-3"));
        pack.Stickers.Should().OnlyContain(sticker => sticker.Origin!.ProjectId == Cat.Id && sticker.Origin.ProjectName == Cat.Name);
    }

    [Fact]
    public async Task Puts_the_emoji_sent_into_the_recent_ones_once_at_the_end()
    {
        await UntilAsync(Start(Request(NewCats)), JobState.Succeeded);

        await _settings.Received(1).RememberEmojiAsync(
            Arg.Is<IReadOnlyList<string>>(emoji => emoji.SequenceEqual(new[] { "🐱", "😂", "🔥", "😴" })), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Waits_when_telegram_asks_and_goes_on_by_itself()
    {
        _telegram.ThenFails("add", FakeTelegram.RateLimited(24));
        var job = Start(Request(NewCats));

        var waiting = await WaitingAsync(job);
        _clock.Advance(TimeSpan.FromSeconds(24));
        var done = await UntilAsync(job, JobState.Succeeded);

        waiting.Progress.RetryAt.Should().Be(_clock.Start.AddSeconds(24));
        waiting.Items.Select(item => item.State).Should().Equal(JobItemState.Done, JobItemState.Sending, JobItemState.Queued);
        Sends.Should().Equal($"create {Cats}", $"add {Cats}", $"add {Cats}", $"add {Cats}");
        done.Items.Should().OnlyContain(item => item.State == JobItemState.Done);
    }

    [Fact]
    public async Task Goes_on_at_once_on_retry_now_while_it_waits()
    {
        _telegram.ThenFails("add", FakeTelegram.RateLimited(60));
        var job = Start(Request(NewCats));
        await WaitingAsync(job);

        var pressed = _queue.Retry(job.Id);
        var done = await UntilAsync(job, JobState.Succeeded);

        pressed.State.Should().Be(JobState.Running);
        done.Progress.RetryAt.Should().BeNull();
    }

    [Fact]
    public async Task Ends_partial_when_telegram_refuses_a_sticker_and_retries_only_that_one()
    {
        _telegram.ThenSucceeds("add");
        _telegram.ThenFails("add", FakeTelegram.Refused("Bad Request: STICKER_VIDEO_LONG"));
        var job = Start(Request(NewCats));

        var partial = await UntilAsync(job, JobState.Partial);
        _queue.Retry(job.Id);
        var done = await UntilAsync(job, JobState.Succeeded);

        partial.Items.Select(item => item.State).Should().Equal(JobItemState.Done, JobItemState.Done, JobItemState.Failed);
        partial.Items[2].Error!.Code.Should().Be(ErrorCode.TelegramError);
        Sends.Should().Equal($"create {Cats}", $"add {Cats}", $"add {Cats}", $"add {Cats}");
        _exported.Should().Equal("seg-1", "seg-2", "seg-3", "seg-3");
        done.Items.Select(item => item.Sticker!.StickerId).Should().Equal("U1", "U2", "U3");
    }

    [Fact]
    public async Task Stops_without_the_network_and_leaves_the_rest_undone()
    {
        _telegram.ThenFails("upload", new AppException(ErrorCode.NetworkUnavailable, "offline"));
        _telegram.Then("upload", () => Task.CompletedTask);
        var job = Start(Request(NewCats));

        // The first upload fails: nothing was sent, the job fails with the network's code
        var failed = await UntilAsync(job, JobState.Failed);

        failed.Error!.Code.Should().Be(ErrorCode.NetworkUnavailable);
        failed.Items.Select(item => item.State).Should().Equal(JobItemState.Failed, JobItemState.Skipped, JobItemState.Skipped);
        Sends.Should().BeEmpty();
        await _settings.DidNotReceive().RememberEmojiAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Takes_nothing_back_from_telegram_when_canceled_on_the_way()
    {
        var hold = new TaskCompletionSource();
        var holding = new TaskCompletionSource();
        var remembered = new TaskCompletionSource();
        _settings.RememberEmojiAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                remembered.TrySetResult();
                return UserSettings.Default(new Folders("/p", "/s"));
            });
        _telegram.ThenSucceeds("upload");
        _telegram.Then("upload", async () =>
        {
            holding.TrySetResult();
            await hold.Task;
        });
        var job = Start(Request(NewCats));
        await holding.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancel);

        var canceled = _queue.Cancel(job.Id);
        hold.SetResult();
        // The executor stops after the held call: «Недавние» get what went before
        await remembered.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancel);

        canceled.Items.Select(item => item.State).Should().Equal(JobItemState.Done, JobItemState.Skipped, JobItemState.Skipped);
        _telegram.Pack(Cats)!.Stickers.Should().ContainSingle();
        _registry.Packs.Single().Stickers.Should().ContainSingle();
    }

    [Fact]
    public async Task Does_not_send_a_file_that_fails_the_checks_and_sends_the_others()
    {
        ExportsAs(segment => segment == "seg-2" ? ValidationVerdict.Fail : ValidationVerdict.Pass);

        var partial = await UntilAsync(Start(Request(NewCats)), JobState.Partial);

        partial.Items.Select(item => item.State).Should().Equal(JobItemState.Done, JobItemState.Skipped, JobItemState.Done);
        partial.Items[1].Error!.Code.Should().Be(ErrorCode.ValidationFailed);
        Sends.Should().HaveCount(2);
        _telegram.Calls.Count(call => call == "upload").Should().Be(2);
    }

    [Fact]
    public async Task Sends_a_fresh_export_as_it_is_after_checking_it_again()
    {
        var export = SampleExports.Of(Cat.Scene.Segments[0], Cat.Source);
        Opens(Cat with { Exports = new Dictionary<string, SegmentExport> { ["seg-1"] = export } });
        _presence.Of(export.Path).Returns(PresenceStatus.Ok);
        _probe.ProbeAsync(export.Path, Arg.Any<CancellationToken>()).Returns(new MediaInfo(
            MediaFormat.Webm, "vp9", 512, 512, new Rational(24, 1), IsVariableFrameRate: false,
            FrameCount: 72, DurationSec: 3, HasAlpha: false, HasAudio: false, SizeBytes: 190_464));

        var done = await UntilAsync(Start(Request(NewCats, Three[0])), JobState.Succeeded);

        _exported.Should().BeEmpty();
        done.Items.Single().Export.Should().BeNull("the job made no new export");
        await _probe.Received(1).ProbeAsync(export.Path, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Replaces_a_sticker_in_its_place()
    {
        _telegram.Has(Cats, PackKind.Regular, 3);
        await _registry.SaveAsync(Pack.Of(_telegram.Pack(Cats)!, _clock.GetUtcNow()), Cancel);
        var request = Request(new PublishTarget.Existing(Cats), new PublishItem("seg-1", ["🐱"], new StickerRef(Cats, "U2")));

        var done = await UntilAsync(Start(request), JobState.Succeeded);

        Sends.Should().Equal($"replace {Cats} F2");
        _telegram.Pack(Cats)!.Stickers.Select(sticker => sticker.UniqueId).Should().Equal("U1", "U4", "U3");
        done.Items.Single().Sticker.Should().Be(new StickerRef(Cats, "U4"));
        _registry.Packs.Single().Stickers[1].Origin!.SegmentId.Should().Be("seg-1");
    }

    [Fact]
    public async Task Adds_to_a_pack_the_first_run_made_when_retried()
    {
        _telegram.ThenFails("add", FakeTelegram.Refused("Bad Request: file is too big"));
        _telegram.ThenFails("add", FakeTelegram.Refused("Bad Request: file is too big"));
        var job = Start(Request(NewCats));
        await UntilAsync(job, JobState.Partial);

        _queue.Retry(job.Id);
        await UntilAsync(job, JobState.Succeeded);

        Sends.Should().Equal($"create {Cats}", $"add {Cats}", $"add {Cats}", $"add {Cats}", $"add {Cats}");
    }

    [Fact]
    public async Task Fails_without_a_linked_account_before_anything_goes()
    {
        _account.GetAsync(Arg.Any<CancellationToken>()).Returns(new AccountLink.Unlinked());

        var failed = await UntilAsync(Start(Request(NewCats)), JobState.Failed);

        failed.Error!.Code.Should().Be(ErrorCode.AccountNotLinked);
        _telegram.Calls.Should().BeEmpty();
        _exported.Should().BeEmpty();
    }

    [Fact]
    public async Task Fails_when_the_scene_was_saved_since()
    {
        Opens(Cat with { Revision = 13 });

        var failed = await UntilAsync(Start(Request(NewCats)), JobState.Failed);

        failed.Error!.Code.Should().Be(ErrorCode.RevisionConflict);
    }
}
