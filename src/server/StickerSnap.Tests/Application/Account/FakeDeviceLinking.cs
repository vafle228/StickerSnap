using StickerSnap.Application.Account;

namespace StickerSnap.Tests.Application.Account;

/// <summary>
/// The linking service without a network. A wait for the answer parks on the test's clock until it
/// times out or <see cref="Answer"/> hands it one; <see cref="ParkedAsync"/> says how many waits
/// have parked, so a test advances the clock only once a wait can see it.
/// </summary>
internal sealed class FakeDeviceLinking(TimeProvider clock) : IDeviceLinking
{
    /// <summary>How long a code lives at the service.</summary>
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);

    private readonly Lock _gate = new();
    private readonly Queue<LinkAnswer> _answers = new();
    private readonly List<LinkInvite> _invites = [];
    private readonly List<LinkInvite> _waits = [];
    private readonly List<LinkInvite> _cancelled = [];
    private readonly List<(int Waits, TaskCompletionSource Done)> _parkWaiters = [];
    private TaskCompletionSource _arrived = NewSignal();
    private int _parked;
    private Exception? _waitFailure;

    /// <summary>What <see cref="InviteAsync"/> throws instead of answering, such as no service.</summary>
    public Exception? InviteFailure { get; set; }

    /// <summary>What <see cref="GetAsync"/> and <see cref="UnlinkAsync"/> throw instead of answering.</summary>
    public Exception? DeviceFailure { get; set; }

    /// <summary>The account the service has for this computer; null when it is not linked.</summary>
    public LinkedDevice? Device { get; set; }

    public int Unlinked { get; private set; }

    public IReadOnlyList<LinkInvite> Invites
    {
        get
        {
            lock (_gate) return [.. _invites];
        }
    }

    /// <summary>The invite of every wait, in order.</summary>
    public IReadOnlyList<LinkInvite> Waits
    {
        get
        {
            lock (_gate) return [.. _waits];
        }
    }

    public IReadOnlyList<LinkInvite> Cancelled
    {
        get
        {
            lock (_gate) return [.. _cancelled];
        }
    }

    /// <summary>The next wait, or the one parked now, ends with <paramref name="answer"/>.</summary>
    public void Answer(LinkAnswer answer)
    {
        lock (_gate)
        {
            _answers.Enqueue(answer);
            _arrived.TrySetResult();
        }
    }

    /// <summary>The next wait fails with <paramref name="failure"/>.</summary>
    public void FailNextWait(Exception failure)
    {
        lock (_gate) _waitFailure = failure;
    }

    /// <summary>Completes once <paramref name="waits"/> waits in all have parked; a guard of 10 s fails a test that hangs.</summary>
    public Task ParkedAsync(int waits = 1)
    {
        lock (_gate)
        {
            if (_parked >= waits) return Task.CompletedTask;
            var done = NewSignal();
            _parkWaiters.Add((waits, done));
            return done.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
    }

    public Task<LinkInvite> InviteAsync(CancellationToken cancellationToken)
    {
        if (InviteFailure is { } failure) return Task.FromException<LinkInvite>(failure);

        lock (_gate)
        {
            var code = $"code{_invites.Count + 1}";
            var invite = new LinkInvite(code, $"https://t.me/StickerSnapAppBot?startapp=link_{code}", clock.GetUtcNow() + CodeLifetime, $"poll-{code}");
            _invites.Add(invite);
            return Task.FromResult(invite);
        }
    }

    public async Task<LinkAnswer> WaitAsync(LinkInvite invite, TimeSpan wait, CancellationToken cancellationToken)
    {
        Task arrived;
        lock (_gate)
        {
            _waits.Add(invite);
            if (_waitFailure is { } failure)
            {
                _waitFailure = null;
                throw failure;
            }
            if (_answers.TryDequeue(out var ready)) return Keep(ready);
            _arrived = NewSignal();
            arrived = _arrived.Task;
        }

        var timeout = Task.Delay(wait, clock, cancellationToken);
        lock (_gate)
        {
            _parked++;
            foreach (var waiter in _parkWaiters.Where(waiter => waiter.Waits <= _parked).ToList())
            {
                waiter.Done.TrySetResult();
                _parkWaiters.Remove(waiter);
            }
        }

        await Task.WhenAny(arrived, timeout);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_answers.TryDequeue(out var answer)) return Keep(answer);
            return clock.GetUtcNow() >= invite.ExpiresAt ? new LinkAnswer.Expired() : new LinkAnswer.Waiting(invite.ExpiresAt);
        }
    }

    public Task CancelAsync(LinkInvite invite, CancellationToken cancellationToken)
    {
        lock (_gate) _cancelled.Add(invite);
        return Task.CompletedTask;
    }

    public Task<LinkedDevice?> GetAsync(CancellationToken cancellationToken) =>
        DeviceFailure is { } failure ? Task.FromException<LinkedDevice?>(failure) : Task.FromResult(Device);

    public Task UnlinkAsync(CancellationToken cancellationToken)
    {
        if (DeviceFailure is { } failure) return Task.FromException(failure);

        Unlinked++;
        Device = null;
        return Task.CompletedTask;
    }

    /// <summary>A linked answer leaves the computer linked, as the real service's key does.</summary>
    private LinkAnswer Keep(LinkAnswer answer)
    {
        if (answer is LinkAnswer.Linked linked) Device = linked.Device;
        return answer;
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
