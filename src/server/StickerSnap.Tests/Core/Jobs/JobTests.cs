using StickerSnap.Core.Errors;
using StickerSnap.Core.Jobs;

namespace StickerSnap.Tests.Core.Jobs;

/// <summary>The life of a job: which moves its states allow, and how its segments decide how it ends.</summary>
public sealed class JobTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly JobError DiskFull = JobError.Of(ErrorCode.DiskFull, "No space left on device");

    private static Job Queued(params string[] segments) =>
        Job.Queue("job-1", JobKind.Export, Guid.Empty, segments.Length > 0 ? segments : ["seg-1", "seg-2"], Now);

    private static Job Running(params string[] segments) => Queued(segments).Start();

    /// <summary>A job in each state, made by the moves that lead to it.</summary>
    private static Job In(JobState state) => state switch
    {
        JobState.Queued => Queued(),
        JobState.Running => Running(),
        JobState.Succeeded => Running().WithItem("seg-1", JobItemState.Done, 1).WithItem("seg-2", JobItemState.Done, 1).Finish(),
        JobState.Partial => Running().WithItem("seg-1", JobItemState.Done, 1).Finish(DiskFull),
        JobState.Failed => Running().Finish(DiskFull),
        JobState.Canceled => Running().Cancel(),
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    [Fact]
    public void Starts_queued_with_a_waiting_item_per_segment()
    {
        var job = Job.Queue("job-1", JobKind.Publish, Guid.Empty, ["seg-1", "seg-2"], Now, "cats_by_StickerSnapAppBot");

        job.State.Should().Be(JobState.Queued);
        job.CreatedAt.Should().Be(Now);
        job.Pack.Should().Be("cats_by_StickerSnapAppBot");
        job.Progress.Should().Be(JobProgress.None);
        job.Items.Should().Equal(
            new JobItem("seg-1", JobItemState.Queued, 0),
            new JobItem("seg-2", JobItemState.Queued, 0));
    }

    [Fact]
    public void Needs_a_segment()
    {
        var queue = () => Job.Queue("job-1", JobKind.Export, Guid.Empty, [], Now);

        queue.Should().Throw<ArgumentException>();
    }

    public static TheoryData<JobState, string, bool> Moves => new()
    {
        { JobState.Queued, "start", true },
        { JobState.Queued, "cancel", true },
        { JobState.Queued, "finish", false },
        { JobState.Queued, "retry", false },
        { JobState.Running, "start", false },
        { JobState.Running, "cancel", true },
        { JobState.Running, "finish", true },
        { JobState.Running, "retry", false },
        { JobState.Succeeded, "start", false },
        { JobState.Succeeded, "cancel", false },
        { JobState.Succeeded, "finish", false },
        { JobState.Succeeded, "retry", false },
        { JobState.Partial, "cancel", false },
        { JobState.Partial, "retry", true },
        { JobState.Failed, "start", false },
        { JobState.Failed, "finish", false },
        { JobState.Failed, "retry", true },
        { JobState.Canceled, "start", false },
        { JobState.Canceled, "cancel", false },
        { JobState.Canceled, "retry", true },
    };

    [Theory]
    [MemberData(nameof(Moves))]
    public void Allows_only_the_moves_of_its_state(JobState state, string move, bool allowed)
    {
        var job = In(state);

        Func<Job> act = move switch
        {
            "start" => job.Start,
            "cancel" => job.Cancel,
            "finish" => () => job.Finish(),
            "retry" => job.Retry,
            _ => throw new ArgumentOutOfRangeException(nameof(move)),
        };

        if (allowed) act.Should().NotThrow();
        else act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(JobState.Queued)]
    [InlineData(JobState.Succeeded)]
    [InlineData(JobState.Canceled)]
    public void Takes_reports_only_while_running(JobState state)
    {
        var job = In(state);

        job.Invoking(j => j.WithItem("seg-1", JobItemState.Exporting, 0.5)).Should().Throw<InvalidOperationException>();
        job.Invoking(j => j.WithStage(JobStage.Pass1)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Takes_the_mean_of_its_segments_as_its_share()
    {
        var job = Running("seg-1", "seg-2", "seg-3", "seg-4")
            .WithItem("seg-1", JobItemState.Done, 0.3)
            .WithItem("seg-2", JobItemState.Exporting, 0.5);

        job.Items[0].Fraction.Should().Be(1, "a segment done is whole");
        job.Progress.Fraction.Should().Be(0.375);
    }

    [Fact]
    public void Refuses_a_segment_it_does_not_have()
    {
        var job = Running();

        job.Invoking(j => j.WithItem("seg-9", JobItemState.Done, 1)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Reports_the_stage_and_the_attempt_without_touching_the_share()
    {
        var attempt = new JobAttempt(2, [new StickerSnap.Core.Render.Concession.Fps(30, 24)]);
        var job = Running().WithItem("seg-1", JobItemState.Exporting, 0.5).WithStage(JobStage.Pass2, attempt);

        job.Progress.Should().Be(new JobProgress(0.25, JobStage.Pass2, attempt));
    }

    [Fact]
    public void Succeeds_when_every_segment_is_done()
    {
        var job = In(JobState.Succeeded);

        job.State.Should().Be(JobState.Succeeded);
        job.Progress.Fraction.Should().Be(1);
        job.Error.Should().BeNull();
    }

    [Fact]
    public void Ends_partial_when_some_segments_are_done_and_fails_the_one_it_stopped_on()
    {
        var job = Running("seg-1", "seg-2", "seg-3")
            .WithItem("seg-1", JobItemState.Done, 1)
            .WithItem("seg-2", JobItemState.Exporting, 0.4)
            .Finish(DiskFull);

        job.State.Should().Be(JobState.Partial);
        job.Error.Should().Be(DiskFull);
        job.Items.Should().Equal(
            new JobItem("seg-1", JobItemState.Done, 1),
            new JobItem("seg-2", JobItemState.Failed, 0.4, DiskFull),
            new JobItem("seg-3", JobItemState.Skipped, 0));
    }

    [Fact]
    public void Ends_partial_with_the_error_of_the_first_segment_that_failed()
    {
        var cannotFit = JobError.Of(ErrorCode.CannotFit, "Smallest attempt 300 KB");
        var job = Running()
            .WithItem("seg-1", JobItemState.Failed, 0.9, cannotFit)
            .WithItem("seg-2", JobItemState.Done, 1)
            .Finish();

        job.State.Should().Be(JobState.Partial);
        job.Error.Should().Be(cannotFit);
    }

    [Fact]
    public void Fails_when_no_segment_is_done()
    {
        var job = Running().Finish(DiskFull);

        job.State.Should().Be(JobState.Failed);
        job.Error.Should().Be(DiskFull);
    }

    [Fact]
    public void Fails_as_internal_when_it_ends_with_nothing_done_and_no_reason()
    {
        var job = Running().Finish();

        job.State.Should().Be(JobState.Failed);
        job.Error!.Code.Should().Be(ErrorCode.Internal);
        job.Items.Should().OnlyContain(item => item.State == JobItemState.Skipped);
    }

    [Fact]
    public void Keeps_what_is_done_and_skips_the_rest_when_canceled()
    {
        var job = Running("seg-1", "seg-2", "seg-3")
            .WithItem("seg-1", JobItemState.Done, 1)
            .WithItem("seg-2", JobItemState.Sending, 0.5)
            .WithStage(JobStage.Wait, retryAt: Now.AddSeconds(24))
            .Cancel();

        job.State.Should().Be(JobState.Canceled);
        job.Progress.RetryAt.Should().BeNull();
        job.Items.Select(item => item.State).Should().Equal(JobItemState.Done, JobItemState.Skipped, JobItemState.Skipped);
    }

    [Fact]
    public void Queues_again_only_the_segments_left_undone_when_retried()
    {
        var job = In(JobState.Partial).Retry();

        job.State.Should().Be(JobState.Queued);
        job.Error.Should().BeNull();
        job.Progress.Should().Be(new JobProgress(0.5, JobStage.Probe));
        job.Items.Should().Equal(
            new JobItem("seg-1", JobItemState.Done, 1),
            new JobItem("seg-2", JobItemState.Queued, 0));
    }
}
