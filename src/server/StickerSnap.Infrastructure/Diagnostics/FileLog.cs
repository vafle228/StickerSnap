using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StickerSnap.Application.Diagnostics;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Infrastructure.Diagnostics;

/// <summary>
/// The server's log on disk: `logs/server-yyyy-MM-dd.log` in the data directory, one file a day
/// (UTC), kept <see cref="KeepDays"/> days. An entry is one line, `2026-10-19T13:22:05.123Z warn
/// JobQueue [trace] message`, and the lines of an exception's stack after it, two spaces in. What
/// the filters of Logging let through is written; a write that fails is dropped, never thrown.
/// </summary>
internal sealed class FileLog(IOptions<DataOptions> options, TimeProvider clock) : ILoggerProvider, ILogArchive
{
    public const string Folder = "logs";
    public const int KeepDays = 7;

    private const string Prefix = "server-";
    private const string Extension = ".log";
    private const string DayFormat = "yyyy-MM-dd";
    private const string StampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

    private readonly Lock _gate = new();
    private DateOnly? _day;

    private string Directory => Path.Combine(options.Value.DataDir, Folder);

    public ILogger CreateLogger(string categoryName) => new Logger(this, ShortName(categoryName));

    public void Dispose()
    {
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ReadAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        var first = DateOnly.FromDateTime(since.UtcDateTime);
        var files = System.IO.Directory.Exists(Directory)
            ? System.IO.Directory.EnumerateFiles(Directory, $"{Prefix}*{Extension}")
                .Select(path => (Path: path, Day: DayOf(path)))
                .Where(file => file.Day >= first)
                .OrderBy(file => file.Day)
                .ToList()
            : [];

        var entries = new List<string>();
        foreach (var file in files)
        {
            string text;
            try
            {
                // The writer appends while this reads: share the file both ways
                await using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                text = await reader.ReadToEndAsync(cancellationToken);
            }
            catch (IOException)
            {
                continue;
            }
            entries.AddRange(Entries(text).Where(entry => StampOf(entry) is not { } at || at >= since));
        }
        return entries;
    }

    internal void Write(LogLevel level, string category, string message, Exception? exception)
    {
        var now = clock.GetUtcNow();
        var entry = new StringBuilder()
            .Append(now.UtcDateTime.ToString(StampFormat, CultureInfo.InvariantCulture))
            .Append(' ').Append(LevelOf(level))
            .Append(' ').Append(category);
        if (Activity.Current?.Id is { } trace) entry.Append(" [").Append(trace).Append(']');
        entry.Append(' ').Append(message.ReplaceLineEndings("\n  "));
        if (exception is not null) entry.Append("\n  ").Append(exception.ToString().ReplaceLineEndings("\n  "));
        entry.Append('\n');

        lock (_gate)
        {
            try
            {
                var day = DateOnly.FromDateTime(now.UtcDateTime);
                System.IO.Directory.CreateDirectory(Directory);
                if (_day != day)
                {
                    _day = day;
                    Prune(day);
                }
                File.AppendAllText(Path.Combine(Directory, NameOf(day)), entry.ToString(), Encoding.UTF8);
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                // A log that cannot be written must not break what was being logged
            }
        }
    }

    /// <summary>Deletes the days older than <see cref="KeepDays"/>.</summary>
    private void Prune(DateOnly today)
    {
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, $"{Prefix}*{Extension}"))
        {
            if (DayOf(path) is { } day && day <= today.AddDays(-KeepDays))
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
                {
                    // Tried again the next day
                }
            }
        }
    }

    internal static string NameOf(DateOnly day) => Prefix + day.ToString(DayFormat, CultureInfo.InvariantCulture) + Extension;

    private static DateOnly? DayOf(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.StartsWith(Prefix, StringComparison.Ordinal) &&
               DateOnly.TryParseExact(name[Prefix.Length..], DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : null;
    }

    /// <summary>The entries of a file: a line that starts with a time begins one, the others go on it.</summary>
    private static IEnumerable<string> Entries(string text)
    {
        var entry = new StringBuilder();
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (StampOf(trimmed) is not null && entry.Length > 0)
            {
                yield return entry.ToString();
                entry.Clear();
            }
            if (trimmed.Length == 0) continue;
            if (entry.Length > 0) entry.Append('\n');
            entry.Append(trimmed);
        }
        if (entry.Length > 0) yield return entry.ToString();
    }

    /// <summary>The time an entry begins with; null for a line that goes on an entry.</summary>
    private static DateTimeOffset? StampOf(string entry) =>
        entry.Length >= StampFormat.Length &&
        DateTimeOffset.TryParseExact(
            entry[..StampFormat.Length], StampFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at)
            ? at
            : null;

    private static string LevelOf(LogLevel level) => level switch
    {
        LogLevel.Trace => "trce",
        LogLevel.Debug => "dbug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "fail",
        _ => "crit",
    };

    /// <summary>`StickerSnap.Application.Jobs.JobQueue` is `JobQueue`; the name of a type is enough to find it.</summary>
    private static string ShortName(string category) =>
        category.LastIndexOf('.') is var dot and >= 0 && dot < category.Length - 1 ? category[(dot + 1)..] : category;

    private sealed class Logger(FileLog log, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            log.Write(logLevel, category, formatter(state, exception), exception);
        }
    }
}
