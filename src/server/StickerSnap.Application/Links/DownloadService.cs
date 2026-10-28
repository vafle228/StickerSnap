using System.Diagnostics;
using Microsoft.Extensions.Logging;
using StickerSnap.Application.Diagnostics;
using StickerSnap.Application.Events;
using StickerSnap.Application.Projects;
using StickerSnap.Application.Sources;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Jobs;
using StickerSnap.Core.Links;
using StickerSnap.Core.Media;

namespace StickerSnap.Application.Links;

/// <summary>
/// The downloads by link, in memory, one at a time: a second one waits in `queued` behind the
/// first. A download checks the room for the file before a byte comes, downloads it into a new
/// upload named after the video's title and makes a project of that upload, as POST /projects
/// does. Every change is sent as downloadUpdated, at most once per <see cref="EventInterval"/>
/// with the latest snapshot; the end of a download at once. A finished one is kept for
/// <see cref="KeepFinished"/>.
/// </summary>
public sealed class DownloadService : IDownloadService, IAsyncDisposable
{
    public static readonly TimeSpan KeepFinished = TimeSpan.FromMinutes(10);

    /// <summary>downloadUpdated no more than 10 times a second.</summary>
    public static readonly TimeSpan EventInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>The longest title kept in the file's name, and so in the project's.</summary>
    internal const int NameLength = 80;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Entry> _downloads = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> _waiting = new();
    private readonly ILinkService _links;
    private readonly IVideoSites _sites;
    private readonly IUploadStore _uploads;
    private readonly IProjectService _projects;
    private readonly IEventPublisher _events;
    private readonly IErrorJournal _journal;
    private readonly TimeProvider _clock;
    private readonly ILogger<DownloadService> _logger;
    private readonly CancellationTokenSource _stopping = new();
    private Entry? _current;
    private long _sequence;
    private bool _disposed;

    public DownloadService(
        ILinkService links,
        IVideoSites sites,
        IUploadStore uploads,
        IProjectService projects,
        IEventPublisher events,
        IErrorJournal journal,
        TimeProvider clock,
        ILogger<DownloadService> logger)
    {
        _links = links;
        _sites = sites;
        _uploads = uploads;
        _projects = projects;
        _events = events;
        _journal = journal;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Download> CreateAsync(string url, CancellationToken cancellationToken)
    {
        var video = await _links.ProbeAsync(url, cancellationToken);
        lock (_gate)
        {
            Purge();
            var entry = new Entry(++_sequence)
            {
                Download = Download.Queue(Guid.NewGuid().ToString(), video, _clock.GetUtcNow()),
                Origin = Activity.Current?.Context ?? default,
            };
            _downloads.Add(entry.Download.Id, entry);
            _waiting.AddLast(entry);
            Publish(entry);
            Pump();
            return entry.Download;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<Download> List()
    {
        lock (_gate)
        {
            Purge();
            return [.. _downloads.Values.OrderByDescending(entry => entry.Sequence).Select(entry => entry.Download)];
        }
    }

    /// <inheritdoc />
    public Download Cancel(string id)
    {
        CancellationTokenSource? running;
        Download download;
        lock (_gate)
        {
            Purge();
            if (!_downloads.TryGetValue(id, out var entry))
                throw new AppException(
                    ErrorCode.JobNotFound,
                    $"There is no download {id}, or it ended more than {KeepFinished.TotalMinutes} minutes ago");
            if (entry.Download.IsOver) return entry.Download;

            _waiting.Remove(entry);
            entry.Download = entry.Download.Cancel();
            Finished(entry);
            running = entry.Cancellation;
            download = entry.Download;
        }
        // Outside the lock: the cancellation runs the download's callbacks on this thread
        Stop(running);
        return download;
    }

    /// <summary>Cancels the download running and waits for it to stop: the app is closing.</summary>
    public async ValueTask DisposeAsync()
    {
        Task? running;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            running = _current?.Task;
        }
        await _stopping.CancelAsync();
        try
        {
            if (running is not null) await running;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "A download did not stop cleanly");
        }
        _stopping.Dispose();
    }

    /// <summary>The name of the file, and so of the project: the video's title.</summary>
    internal static string FileNameOf(LinkVideo video)
    {
        var title = SafeFileNames.Of(video.Title.Replace('/', ' ').Replace('\\', ' '));
        if (title.Length > NameLength) title = title[..NameLength].TrimEnd('.', ' ');
        return $"{title}.{video.Extension}";
    }

    /// <summary>Starts the oldest waiting download on the thread pool when none is running.</summary>
    private void Pump()
    {
        if (_disposed || _current is not null || _waiting.First?.Value is not { } entry) return;
        _waiting.RemoveFirst();

        entry.Download = entry.Download.Start();
        entry.Cancellation = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
        _current = entry;
        Publish(entry);
        entry.Task = Task.Run(() => RunAsync(entry, entry.Cancellation.Token));
    }

    private async Task RunAsync(Entry entry, CancellationToken cancellationToken)
    {
        Activity.Current = null;
        ActivityLink[]? links = entry.Origin == default ? null : [new ActivityLink(entry.Origin)];
        using var activity = Telemetry.Source.StartActivity("download", ActivityKind.Internal, parentContext: default, links: links);
        var video = entry.Download.Video;
        activity?.SetTag("stickersnap.download.id", entry.Download.Id);
        activity?.SetTag("stickersnap.download.site", video.Site.ToString().ToLowerInvariant());

        UploadSlot? slot = null;
        try
        {
            var free = _uploads.FreeBytes();
            if (video.Bytes > free) throw DiskFull(video, free);

            slot = _uploads.Reserve(FileNameOf(video));
            var bytes = await _sites.DownloadAsync(
                video,
                slot.Path,
                progress => Change(entry, download => download.Receive(progress.ReceivedBytes, progress.EtaSec)),
                cancellationToken);

            Change(entry, download => download.Read(bytes));
            var opened = await _projects.CreateAsync(new SourceRef.Upload(slot.UploadId), cancellationToken);
            Change(entry, download => download.Succeed(opened.Project.Id));
            slot = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Canceled from outside: the download has its end already
        }
        catch (Exception exception)
        {
            if (exception is AppException { Code: ErrorCode.DiskFull } full && !full.Params.ContainsKey("needBytes"))
                exception = DiskFull(video, _uploads.FreeBytes(), full);
            if (exception is not AppException)
                _logger.LogError(exception, "Download {Download} of {Url} failed", entry.Download.Id, video.Url);

            var error = exception is AppException known ? JobError.Of(known) : JobError.Of(ErrorCode.Internal, exception.Message);
            Change(entry, download => download.Fail(error));
            activity?.SetStatus(ActivityStatusCode.Error, error.Code.ToString());
            _journal.Record(ErrorRecord.Of(
                exception, entry.Download.Id, _clock.GetUtcNow(), "download", new Dictionary<string, string> { ["url"] = video.Url }));
        }
        finally
        {
            if (slot is not null) _uploads.Discard(slot.UploadId);
            lock (_gate)
            {
                if (_current == entry) _current = null;
                entry.Cancellation?.Dispose();
                entry.Cancellation = null;
                Pump();
            }
        }
    }

    /// <summary>A change of the running download; dropped once it ended, such as after «Отменить».</summary>
    private void Change(Entry entry, Func<Download, Download> change)
    {
        lock (_gate)
        {
            if (entry.Download.IsOver) return;
            entry.Download = change(entry.Download);
            if (entry.Download.IsOver) Finished(entry);
            else Publish(entry);
        }
    }

    private static AppException DiskFull(LinkVideo video, long free, Exception? inner = null) =>
        new(ErrorCode.DiskFull, $"Need {video.Bytes} bytes for {video.Url}, {free} free", inner)
        {
            Params = new Dictionary<string, object> { ["needBytes"] = video.Bytes, ["freeBytes"] = free },
            Log = $"Need {video.Bytes} bytes for «{video.Title}», {free} free",
        };

    private void Finished(Entry entry)
    {
        entry.FinishedAt = _clock.GetUtcNow();
        Publish(entry);
    }

    /// <summary>
    /// Sends the download now when it has ended or its last event is old enough; otherwise once
    /// the interval is up, with whatever it is by then.
    /// </summary>
    private void Publish(Entry entry)
    {
        var now = _clock.GetUtcNow();
        if (entry.Download.IsOver || entry.SentAt is not { } sent || now - sent >= EventInterval)
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

    /// <summary>Starts sending under the lock, so the hub gets the snapshots of a download in their order.</summary>
    private void Send(Entry entry, DateTimeOffset now)
    {
        entry.Pending?.Dispose();
        entry.Pending = null;
        entry.SentAt = now;
        _ = ObserveAsync(_events.DownloadUpdatedAsync(entry.Download, CancellationToken.None), entry.Download.Id);
    }

    private async Task ObserveAsync(Task sending, string id)
    {
        try
        {
            await sending;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "downloadUpdated of {Download} was not sent", id);
        }
    }

    /// <summary>Forgets the downloads that ended more than <see cref="KeepFinished"/> ago.</summary>
    private void Purge()
    {
        var now = _clock.GetUtcNow();
        foreach (var entry in _downloads.Values.Where(entry => now - entry.FinishedAt >= KeepFinished).ToList())
        {
            entry.Pending?.Dispose();
            _downloads.Remove(entry.Download.Id);
        }
    }

    private static void Stop(CancellationTokenSource? running)
    {
        try
        {
            running?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The download ended in the meantime.
        }
    }

    private sealed class Entry(long sequence)
    {
        public long Sequence { get; } = sequence;

        public required Download Download { get; set; }

        /// <summary>The span of the request that queued the download, for the download's own span to link to.</summary>
        public ActivityContext Origin { get; init; }

        /// <summary>Stops the download while it runs; null while it waits and once it stopped.</summary>
        public CancellationTokenSource? Cancellation { get; set; }

        public Task Task { get; set; } = Task.CompletedTask;

        public DateTimeOffset? FinishedAt { get; set; }

        public DateTimeOffset? SentAt { get; set; }

        /// <summary>A downloadUpdated held back by the interval.</summary>
        public ITimer? Pending { get; set; }
    }
}
