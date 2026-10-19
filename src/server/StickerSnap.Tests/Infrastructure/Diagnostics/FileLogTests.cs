using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using StickerSnap.Infrastructure.Diagnostics;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Tests.Infrastructure.Diagnostics;

/// <summary>The server's log in a temporary data directory: a file a day, 7 days kept, read back by time.</summary>
public sealed class FileLogTests : IDisposable
{
    private readonly DirectoryInfo _data = Directory.CreateTempSubdirectory("stickersnap-log-");
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 19, 13, 22, 5, 123, TimeSpan.Zero));
    private readonly FileLog _log;

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public FileLogTests()
    {
        _log = new FileLog(Options.Create(new DataOptions { DataDir = _data.FullName }), _clock);
    }

    public void Dispose() => _data.Delete(recursive: true);

    private string Logs => Path.Combine(_data.FullName, FileLog.Folder);

    private ILogger Logger => _log.CreateLogger("StickerSnap.Application.Jobs.JobQueue");

    [Fact]
    public async Task Writes_an_entry_a_line_with_the_stack_of_its_exception_under_it()
    {
        Logger.LogWarning("Job {Job} failed", "job-1");
        Logger.LogError(new InvalidOperationException("boom"), "Unhandled");

        var text = await File.ReadAllTextAsync(Path.Combine(Logs, "server-2026-10-19.log"), Cancel);
        text.Should().StartWith("2026-10-19T13:22:05.123Z warn JobQueue Job job-1 failed\n2026-10-19T13:22:05.123Z fail JobQueue Unhandled\n  System.InvalidOperationException: boom\n");

        var entries = await _log.ReadAsync(_clock.GetUtcNow(), Cancel);
        entries.Should().HaveCount(2);
        entries[1].Should().Be("2026-10-19T13:22:05.123Z fail JobQueue Unhandled\n  System.InvalidOperationException: boom");
    }

    [Fact]
    public async Task Reads_back_what_was_written_since_a_time_across_days()
    {
        Logger.LogInformation("yesterday");
        _clock.Advance(TimeSpan.FromHours(10));
        Logger.LogInformation("just before midnight");
        _clock.Advance(TimeSpan.FromHours(2));
        Logger.LogInformation("after midnight");

        var entries = await _log.ReadAsync(_clock.GetUtcNow().AddHours(-2), Cancel);

        entries.Should().Equal(
            "2026-10-19T23:22:05.123Z info JobQueue just before midnight",
            "2026-10-20T01:22:05.123Z info JobQueue after midnight");
        File.Exists(Path.Combine(Logs, "server-2026-10-20.log")).Should().BeTrue();
    }

    [Fact]
    public async Task Keeps_the_files_of_the_last_7_days()
    {
        Directory.CreateDirectory(Logs);
        await File.WriteAllTextAsync(Path.Combine(Logs, "server-2026-10-12.log"), "old\n", Cancel);
        await File.WriteAllTextAsync(Path.Combine(Logs, "server-2026-10-13.log"), "kept\n", Cancel);
        await File.WriteAllTextAsync(Path.Combine(Logs, "notes.txt"), "not ours\n", Cancel);

        Logger.LogInformation("today");

        Directory.GetFiles(Logs).Select(Path.GetFileName).Should().BeEquivalentTo("server-2026-10-13.log", "server-2026-10-19.log", "notes.txt");
    }

    [Fact]
    public async Task Reads_nothing_before_anything_was_written()
    {
        (await _log.ReadAsync(_clock.GetUtcNow().AddHours(-1), Cancel)).Should().BeEmpty();
    }
}
