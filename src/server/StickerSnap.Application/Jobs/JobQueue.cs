using Microsoft.Extensions.Logging;
using StickerSnap.Application.Events;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Jobs;

namespace StickerSnap.Application.Jobs;

/// <summary>
/// The background jobs, in memory (contract, «Фоновые задачи»). Two lanes, one job running in
/// each: previews, and exports with publications, so a second publication waits in `queued`
/// behind the first. A new preview of a project cancels the one still queued or running for it.
/// The work is done by the <see cref="IJobExecutor"/> of the job's kind. Every change is sent as
/// jobUpdated, at most once per <see cref="EventInterval"/> a job with its latest snapshot; the
/// end of a job is sent at once. A finished job is kept for <see cref="KeepFinished"/>.
/// </summary>
public sealed class JobQueue : IJobQueue, IAsyncDisposable
{
    public static readonly TimeSpan KeepFinished = TimeSpan.FromMinutes(10);

    /// <summary>jobUpdated no more than 10 times a second.</summary>
    public static readonly TimeSpan EventInterval = TimeSpan.FromMilliseconds(100);

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _jobs = new(StringComparer.Ordinal);
    private readonly Lane _previews = new();
    private readonly Lane _outputs = new();
    private readonly Dictionary<JobKind, IJobExecutor> _executors;
    private readonly IEventPublisher _events;
    private readonly TimeProvider _clock;
    private readonly ILogger<JobQueue> _logger;
    private readonly CancellationTokenSource _stopping = new();
    private long _sequence;
    private bool _disposed;

    public JobQueue(IEnumerable<IJobExecutor> executors, IEventPublisher events, TimeProvider clock, ILogger<JobQueue> logger)
    {
        _executors = executors.ToDictionary(executor => executor.Kind);
        _events = events;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Job Enqueue(JobRequest request)
    {
        var pack = request is JobRequest.Publish publish ? publish.Target.PackName : null;
        var stopped = new List<Run>();
        Job job;
        lock (_gate)
        {
            Purge();
            var id = Guid.NewGuid().ToString();
            var entry = new Entry(request, ++_sequence, LaneOf(request.Kind))
            {
                Job = Job.Queue(id, request.Kind, request.ProjectId, request.SegmentIds, _clock.GetUtcNow(), pack),
            };
            _jobs.Add(id, entry);
            stopped.AddRange(Supersede(entry));
            job = Wait(entry);
        }
        Stop(stopped);
        return job;
    }

    /// <inheritdoc/>
    public IReadOnlyList<Job> List()
    {
        lock (_gate)
        {
            Purge();
            return [.. _jobs.Values.OrderByDescending(entry => entry.Sequence).Select(entry => entry.Job)];
        }
    }

    /// <inheritdoc/>
    public Job Get(string id)
    {
        lock (_gate) return Find(id).Job;
    }

    /// <inheritdoc/>
    public JobRequest RequestOf(string id)
    {
        lock (_gate) return Find(id).Request;
    }

    /// <inheritdoc/>
    public Job Cancel(string id)
    {
        Run? stopped;
        Job job;
        lock (_gate)
        {
            var entry = Find(id);
            stopped = CancelUnderLock(entry);
            job = entry.Job;
        }
        Stop(stopped is null ? [] : [stopped]);
        return job;
    }

    /// <inheritdoc/>
    public Job Retry(string id)
    {
        var stopped = new List<Run>();
        Job job;
        lock (_gate)
        {
            var entry = Find(id);
            if (!entry.Job.IsOver)
            {
                // «Повторить сейчас» while Telegram asked to wait: the run goes on at once
                entry.Run?.Handle?.WakeUp();
                return entry.Job;
            }
            if (!entry.Job.CanRetry)
                throw new AppException(ErrorCode.BadRequest, $"Job {id} succeeded: there is nothing to retry");

            entry.Job = entry.Job.Retry();
            entry.FinishedAt = null;
            stopped.AddRange(Supersede(entry));
            job = Wait(entry);
        }
        Stop(stopped);
        return job;
    }

    /// <summary>Cancels every job still running and waits for the executors to stop: the app is closing.</summary>
    public async ValueTask DisposeAsync()
    {
        Task[] running;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            running = [.. new[] { _previews.Current, _outputs.Current }.OfType<Run>().Select(run => run.Task)];
        }
        await _stopping.CancelAsync();
        try
        {
            await Task.WhenAll(running);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "A job did not stop cleanly");
        }
        _stopping.Dispose();
    }

    internal TimeProvider Clock => _clock;

    internal Job SnapshotOf(JobRun run)
    {
        lock (_gate) return run.Entry.Job;
    }

    /// <summary>A change an executor reports; dropped once its run is no longer the job's current one.</summary>
    internal void Report(JobRun run, Func<Job, Job> change)
    {
        lock (_gate)
        {
            var entry = run.Entry;
            if (entry.Run?.Handle != run || entry.Job.State != JobState.Running) return;
            entry.Job = change(entry.Job);
            Publish(entry);
        }
    }

    private Lane LaneOf(JobKind kind) => kind == JobKind.Preview ? _previews : _outputs;

    private Entry Find(string id)
    {
        Purge();
        return _jobs.TryGetValue(id, out var entry)
            ? entry
            : throw new AppException(
                ErrorCode.JobNotFound, $"There is no job {id}, or it ended more than {KeepFinished.TotalMinutes} minutes ago");
    }

    /// <summary>A new or retried preview cancels the other previews of its project that have not ended.</summary>
    private List<Run> Supersede(Entry entry)
    {
        var stopped = new List<Run>();
        if (entry.Request.Kind != JobKind.Preview) return stopped;

        var others = _jobs.Values
            .Where(other => other != entry && other.Request.Kind == JobKind.Preview &&
                            other.Request.ProjectId == entry.Request.ProjectId && !other.Job.IsOver)
            .ToList();
        foreach (var other in others)
            if (CancelUnderLock(other) is { } run) stopped.Add(run);
        return stopped;
    }

    /// <summary>Puts the job at the end of its lane and starts the lane if it is free.</summary>
    private Job Wait(Entry entry)
    {
        entry.Lane.Waiting.AddLast(entry);
        Publish(entry);
        Pump(entry.Lane);
        return entry.Job;
    }

    /// <summary>Cancels the job; the run to tell to stop, when it was running.</summary>
    private Run? CancelUnderLock(Entry entry)
    {
        if (entry.Job.IsOver) return null;
        entry.Lane.Waiting.Remove(entry);
        entry.Job = entry.Job.Cancel();
        var run = entry.Run;
        entry.Run = null;
        Finished(entry);
        return run;
    }

    /// <summary>Starts the next job of a free lane on the thread pool.</summary>
    private void Pump(Lane lane)
    {
        if (lane.Current is not null || lane.Waiting.First?.Value is not { } entry) return;
        lane.Waiting.RemoveFirst();

        entry.Job = entry.Job.Start();
        var run = new Run(CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token));
        run.Handle = new JobRun(this, entry);
        entry.Run = run;
        lane.Current = run;
        Publish(entry);
        run.Task = Task.Run(() => ExecuteAsync(entry, run));
    }

    private async Task ExecuteAsync(Entry entry, Run run)
    {
        JobError? error = null;
        var canceled = false;
        try
        {
            if (!_executors.TryGetValue(entry.Request.Kind, out var executor))
                throw new AppException(ErrorCode.Internal, $"Nothing runs {entry.Request.Kind} jobs yet");
            await executor.RunAsync(run.Handle!, run.Cancellation.Token);
        }
        catch (OperationCanceledException) when (run.Cancellation.IsCancellationRequested)
        {
            canceled = true;
        }
        catch (AppException exception)
        {
            error = JobError.Of(exception);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Job {Job} ({Kind}) failed", entry.Job.Id, entry.Request.Kind);
            error = JobError.Of(ErrorCode.Internal, exception.Message);
        }

        lock (_gate)
        {
            if (entry.Run == run)
            {
                entry.Run = null;
                entry.Job = canceled ? entry.Job.Cancel() : entry.Job.Finish(error);
                Finished(entry);
            }
            entry.Lane.Current = null;
            Pump(entry.Lane);
        }
        run.Cancellation.Dispose();
    }

    private void Finished(Entry entry)
    {
        entry.FinishedAt = _clock.GetUtcNow();
        Publish(entry);
    }

    /// <summary>
    /// Sends the job now when it has ended or its last event is old enough; otherwise once the
    /// interval is up, with whatever the job is by then.
    /// </summary>
    private void Publish(Entry entry)
    {
        var now = _clock.GetUtcNow();
        if (entry.Job.IsOver || entry.SentAt is not { } sent || now - sent >= EventInterval)
            Send(entry, now);
        else
            entry.Pending ??= _clock.CreateTimer(_ => Flush(entry), null, sent + EventInterval - now, Timeout.InfiniteTimeSpan);
    }

    private void Flush(Entry entry)
    {
        lock (_gate)
        {
            if (entry.Pending is not null) Send(entry, _clock.GetUtcNow());
        }
    }

    /// <summary>Starts sending under the lock, so the hub gets the snapshots of a job in their order.</summary>
    private void Send(Entry entry, DateTimeOffset now)
    {
        entry.Pending?.Dispose();
        entry.Pending = null;
        entry.SentAt = now;
        _ = ObserveAsync(_events.JobUpdatedAsync(entry.Job, CancellationToken.None), entry.Job.Id);
    }

    private async Task ObserveAsync(Task sending, string id)
    {
        try
        {
            await sending;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "jobUpdated of {Job} was not sent", id);
        }
    }

    /// <summary>Forgets the jobs that ended more than <see cref="KeepFinished"/> ago.</summary>
    private void Purge()
    {
        var now = _clock.GetUtcNow();
        foreach (var entry in _jobs.Values.Where(entry => now - entry.FinishedAt >= KeepFinished).ToList())
        {
            entry.Pending?.Dispose();
            _jobs.Remove(entry.Job.Id);
        }
    }

    /// <summary>Tells executors to stop outside the lock: a cancellation runs their callbacks on this thread.</summary>
    private static void Stop(IEnumerable<Run> runs)
    {
        foreach (var run in runs)
        {
            try
            {
                run.Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The run ended in the meantime.
            }
        }
    }

    internal sealed class Entry(JobRequest request, long sequence, Lane lane)
    {
        public JobRequest Request { get; } = request;

        public long Sequence { get; } = sequence;

        public Lane Lane { get; } = lane;

        public required Job Job { get; set; }

        /// <summary>The run going on for this job; null while it waits, once it ended, and once it was canceled.</summary>
        public Run? Run { get; set; }

        public DateTimeOffset? FinishedAt { get; set; }

        public DateTimeOffset? SentAt { get; set; }

        /// <summary>A jobUpdated held back by the interval.</summary>
        public ITimer? Pending { get; set; }
    }

    internal sealed class Lane
    {
        public LinkedList<Entry> Waiting { get; } = new();

        /// <summary>The run holding the lane until its executor returns, even when its job was canceled.</summary>
        public Run? Current { get; set; }
    }

    internal sealed class Run(CancellationTokenSource cancellation)
    {
        public CancellationTokenSource Cancellation { get; } = cancellation;

        public JobRun? Handle { get; set; }

        public Task Task { get; set; } = Task.CompletedTask;
    }
}
