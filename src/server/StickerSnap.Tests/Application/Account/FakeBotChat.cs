using StickerSnap.Application.Account;

namespace StickerSnap.Tests.Application.Account;

/// <summary>
/// The bot's chat without a messenger. A read of the messages waits on the test's clock until it
/// times out or <see cref="Deliver"/> hands it messages; <see cref="ParkedAsync"/> says how many
/// reads have waited, so a test advances the clock only once a read can see it.
/// </summary>
internal sealed class FakeBotChat(TimeProvider clock) : IBotChat
{
    public static readonly ChatUser Bot = new(7000000001, "StickerSnap", null, "StickerSnapAppBot");

    private readonly Lock _gate = new();
    private readonly Queue<IReadOnlyList<ChatMessage>> _inbox = new();
    private readonly List<long?> _reads = [];
    private readonly List<(long ChatId, string Text)> _sent = [];
    private TaskCompletionSource _arrived = NewSignal();
    private readonly List<(int Reads, TaskCompletionSource Done)> _parkWaiters = [];
    private int _parkedReads;
    private Exception? _readFailure;

    /// <summary>What <see cref="GetBotAsync"/> throws instead of answering, such as no token.</summary>
    public Exception? BotFailure { get; set; }

    /// <summary>What <see cref="CheckChatAsync"/> throws instead of answering, such as a blocked bot.</summary>
    public Exception? ChatFailure { get; set; }

    /// <summary>The `after` of every read, in order.</summary>
    public IReadOnlyList<long?> Reads
    {
        get
        {
            lock (_gate) return [.. _reads];
        }
    }

    public IReadOnlyList<(long ChatId, string Text)> Sent
    {
        get
        {
            lock (_gate) return [.. _sent];
        }
    }

    public void Deliver(params ChatMessage[] messages)
    {
        lock (_gate)
        {
            _inbox.Enqueue(messages);
            _arrived.TrySetResult();
        }
    }

    /// <summary>The next read fails with <paramref name="failure"/>.</summary>
    public void FailNextRead(Exception failure)
    {
        lock (_gate) _readFailure = failure;
    }

    /// <summary>Completes once <paramref name="reads"/> reads in all have waited for messages; a guard of 10 s fails a test that hangs.</summary>
    public Task ParkedAsync(int reads = 1)
    {
        lock (_gate)
        {
            if (_parkedReads >= reads) return Task.CompletedTask;
            var done = NewSignal();
            _parkWaiters.Add((reads, done));
            return done.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
    }

    public Task<ChatUser> GetBotAsync(CancellationToken cancellationToken) =>
        BotFailure is { } failure ? Task.FromException<ChatUser>(failure) : Task.FromResult(Bot);

    public async Task<IReadOnlyList<ChatMessage>> ReceiveAsync(long? after, TimeSpan wait, CancellationToken cancellationToken)
    {
        Task arrived;
        lock (_gate)
        {
            _reads.Add(after);
            if (_readFailure is { } failure)
            {
                _readFailure = null;
                throw failure;
            }
            if (_inbox.TryDequeue(out var ready)) return ready;
            _arrived = NewSignal();
            arrived = _arrived.Task;
        }

        var timeout = Task.Delay(wait, clock, cancellationToken);
        lock (_gate)
        {
            _parkedReads++;
            foreach (var waiter in _parkWaiters.Where(waiter => waiter.Reads <= _parkedReads).ToList())
            {
                waiter.Done.TrySetResult();
                _parkWaiters.Remove(waiter);
            }
        }

        await Task.WhenAny(arrived, timeout);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return _inbox.TryDequeue(out var messages) ? messages : [];
    }

    public Task SendAsync(long chatId, string text, CancellationToken cancellationToken)
    {
        lock (_gate) _sent.Add((chatId, text));
        return Task.CompletedTask;
    }

    public Task CheckChatAsync(long chatId, CancellationToken cancellationToken) =>
        ChatFailure is { } failure ? Task.FromException(failure) : Task.CompletedTask;

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
