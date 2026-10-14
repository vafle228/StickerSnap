using System.Threading.Channels;
using StickerSnap.Application.Events;
using StickerSnap.Application.Jobs;
using StickerSnap.Application.Projects;
using StickerSnap.Core.Jobs;
using StickerSnap.Core.Settings;

namespace StickerSnap.Tests.Application.Jobs;

/// <summary>
/// An executor the test drives: each run waits until the test ends it, and is handed over by
/// <see cref="NextAsync"/>. A canceled run throws as a real executor does.
/// </summary>
internal sealed class ControlledExecutor(JobKind kind) : IJobExecutor
{
    private readonly Channel<ExecutorCall> _calls = Channel.CreateUnbounded<ExecutorCall>();

    private int _runs;

    public JobKind Kind { get; } = kind;

    public int Runs => Volatile.Read(ref _runs);

    public async Task RunAsync(JobRun run, CancellationToken cancellationToken)
    {
        var call = new ExecutorCall(run, cancellationToken);
        Interlocked.Increment(ref _runs);
        await _calls.Writer.WriteAsync(call, cancellationToken);
        await call.Ended.WaitAsync(cancellationToken);
    }

    /// <summary>The next run the queue starts; a guard, not a wait: it fails a test that hangs.</summary>
    public Task<ExecutorCall> NextAsync() =>
        _calls.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(10));

    /// <summary>Whether a run was started and not yet taken.</summary>
    public bool HasWaitingCall => _calls.Reader.TryPeek(out _);
}

internal sealed class ExecutorCall(JobRun run, CancellationToken cancellation)
{
    private readonly TaskCompletionSource _end = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public JobRun Run { get; } = run;

    public CancellationToken Cancellation { get; } = cancellation;

    public Task Ended => _end.Task;

    /// <summary>Every segment left to do is done, and the executor returns.</summary>
    public void Complete()
    {
        foreach (var segment in Run.SegmentsToDo) Run.Item(segment, JobItemState.Done, 1);
        _end.TrySetResult();
    }

    public void Return() => _end.TrySetResult();

    public void Throw(Exception exception) => _end.TrySetException(exception);
}

/// <summary>The jobUpdated events the queue sends, and a way to await one.</summary>
internal sealed class RecordingPublisher : IEventPublisher
{
    private readonly Lock _gate = new();
    private readonly List<Job> _sent = [];
    private readonly List<(Func<Job, bool> Match, TaskCompletionSource<Job> Found)> _awaited = [];

    public IReadOnlyList<Job> Sent(string id)
    {
        lock (_gate) return [.. _sent.Where(job => job.Id == id)];
    }

    /// <summary>The first job sent that matches, now or later; a guard of 10 s fails a test that hangs.</summary>
    public Task<Job> UntilAsync(Func<Job, bool> match)
    {
        lock (_gate)
        {
            if (_sent.FirstOrDefault(match) is { } sent) return Task.FromResult(sent);
            var found = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);
            _awaited.Add((match, found));
            return found.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
    }

    public Task<Job> UntilAsync(string id, JobState state) => UntilAsync(job => job.Id == id && job.State == state);

    public Task AccountChangedAsync(StickerSnap.Core.Account.AccountLink link, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task JobUpdatedAsync(Job job, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _sent.Add(job);
            foreach (var awaited in _awaited.Where(awaited => awaited.Match(job)).ToList())
            {
                awaited.Found.TrySetResult(job);
                _awaited.Remove(awaited);
            }
        }
        return Task.CompletedTask;
    }

    public Task SettingsChangedAsync(UserSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ProjectChangedAsync(ProjectListItem project, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ProjectRemovedAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task PackChangedAsync(StickerSnap.Core.Packs.Pack pack, CancellationToken cancellationToken) => Task.CompletedTask;
}
