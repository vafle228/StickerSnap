using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using StickerSnap.Core.Errors;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Tests.Infrastructure.Storage;

/// <summary>The media cache in a temporary data directory, with files written by the test itself.</summary>
public sealed class MediaCacheTests : IDisposable
{
    private const string Key = "0123456789abcdef0123456789abcdef";

    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("stickersnap-media-");
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
    private readonly MediaCache _cache;
    private int _writes;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public MediaCacheTests()
    {
        _cache = new MediaCache(Options.Create(new DataOptions { DataDir = _data.FullName }), _clock, NullLogger<MediaCache>.Instance);
    }

    public void Dispose() => _data.Delete(recursive: true);

    private async Task WriteAsync(string path, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _writes);
        await File.WriteAllTextAsync(path, "strip", cancellationToken);
    }

    [Fact]
    public async Task Makes_a_file_once_and_finds_it_after()
    {
        var made = await _cache.GetOrAddAsync(Key, "webp", WriteAsync, Cancel);
        var again = await _cache.GetOrAddAsync(Key, "webp", WriteAsync, Cancel);

        made.Should().Be(Path.Combine(_cache.Dir, $"{Key}.webp"));
        again.Should().Be(made);
        _cache.Find(Key, "webp").Should().Be(made);
        _writes.Should().Be(1);
        (await File.ReadAllTextAsync(made, Cancel)).Should().Be("strip");
    }

    [Fact]
    public void Finds_nothing_that_was_never_made()
    {
        _cache.Find(Key, "webp").Should().BeNull();
    }

    [Fact]
    public async Task Makes_a_file_asked_for_twice_at_once_only_once()
    {
        var gate = new TaskCompletionSource();
        async Task SlowAsync(string path, CancellationToken cancellationToken)
        {
            await gate.Task;
            await WriteAsync(path, cancellationToken);
        }

        var first = _cache.GetOrAddAsync(Key, "webp", SlowAsync, Cancel);
        var second = _cache.GetOrAddAsync(Key, "webp", SlowAsync, Cancel);
        gate.SetResult();

        (await Task.WhenAll(first, second)).Distinct().Should().ContainSingle();
        _writes.Should().Be(1);
    }

    [Fact]
    public async Task Goes_on_making_a_file_its_caller_stopped_waiting_for()
    {
        var gate = new TaskCompletionSource();
        async Task SlowAsync(string path, CancellationToken cancellationToken)
        {
            await gate.Task;
            await WriteAsync(path, cancellationToken);
        }

        using var dropped = new CancellationTokenSource();
        var waiting = _cache.GetOrAddAsync(Key, "webp", SlowAsync, dropped.Token);
        await dropped.CancelAsync();
        var wait = () => waiting;
        await wait.Should().ThrowAsync<OperationCanceledException>();

        var next = _cache.GetOrAddAsync(Key, "webp", SlowAsync, Cancel);
        gate.SetResult();

        File.Exists(await next).Should().BeTrue();
        _writes.Should().Be(1);
    }

    [Fact]
    public async Task Keeps_nothing_of_a_failed_making_and_tries_again_next_time()
    {
        Task FailAsync(string path, CancellationToken cancellationToken)
        {
            File.WriteAllText(path, "half");
            throw new AppException(ErrorCode.RenderFailed, "the engine failed");
        }

        var failed = () => _cache.GetOrAddAsync(Key, "webp", FailAsync, Cancel);

        (await failed.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.RenderFailed);
        Directory.EnumerateFiles(_cache.Dir).Should().BeEmpty();
        await _cache.GetOrAddAsync(Key, "webp", WriteAsync, Cancel);
        _writes.Should().Be(1);
    }

    [Fact]
    public async Task Removes_files_nobody_asked_for_in_a_month_and_keeps_those_asked_for()
    {
        const string Asked = "11111111111111111111111111111111";
        const string Forgotten = "22222222222222222222222222222222";
        await _cache.GetOrAddAsync(Asked, "webp", WriteAsync, Cancel);
        await _cache.GetOrAddAsync(Forgotten, "webp", WriteAsync, Cancel);
        File.SetLastWriteTimeUtc(Path.Combine(_cache.Dir, $"{Asked}.webp"), _clock.GetUtcNow().UtcDateTime);
        File.SetLastWriteTimeUtc(Path.Combine(_cache.Dir, $"{Forgotten}.webp"), _clock.GetUtcNow().UtcDateTime);

        _clock.Advance(TimeSpan.FromDays(20));
        _cache.Find(Asked, "webp").Should().NotBeNull();
        _clock.Advance(TimeSpan.FromDays(11));
        await _cache.GetOrAddAsync(Key, "webp", WriteAsync, Cancel);

        _cache.Find(Asked, "webp").Should().NotBeNull();
        _cache.Find(Forgotten, "webp").Should().BeNull();
    }

    [Theory]
    [InlineData("../../settings")]
    [InlineData("ABCDEF")]
    [InlineData("")]
    public void Refuses_a_key_that_is_not_one_of_its_own(string key)
    {
        var find = () => _cache.Find(key, "webp");

        find.Should().Throw<ArgumentException>();
    }
}
