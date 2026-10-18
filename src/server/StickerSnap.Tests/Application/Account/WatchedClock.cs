using Microsoft.Extensions.Time.Testing;

namespace StickerSnap.Tests.Application.Account;

/// <summary>
/// The test's clock that says when a timer starts, so a test advances it only once a pause such as
/// Task.Delay can see the advance, and never past what the code under test waits for.
/// </summary>
internal sealed class WatchedClock(DateTimeOffset start) : FakeTimeProvider(start)
{
    private readonly Lock _gate = new();
    private readonly List<TimeSpan> _started = [];
    private readonly List<(TimeSpan DueTime, TaskCompletionSource Done)> _waiters = [];

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        // Created first: an advance right after the signal fires it
        var timer = base.CreateTimer(callback, state, dueTime, period);
        lock (_gate)
        {
            _started.Add(dueTime);
            foreach (var waiter in _waiters.Where(waiter => waiter.DueTime == dueTime).ToList())
            {
                waiter.Done.TrySetResult();
                _waiters.Remove(waiter);
            }
        }

        return timer;
    }

    /// <summary>Completes once a timer of <paramref name="dueTime"/> has started; a guard of 10 s fails a test that hangs.</summary>
    public Task TimerStartedAsync(TimeSpan dueTime)
    {
        lock (_gate)
        {
            if (_started.Contains(dueTime)) return Task.CompletedTask;
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Add((dueTime, done));
            return done.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
    }
}
