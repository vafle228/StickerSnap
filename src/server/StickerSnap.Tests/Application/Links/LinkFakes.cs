using System.Threading.Channels;
using StickerSnap.Application.Events;
using StickerSnap.Application.Links;
using StickerSnap.Application.Projects;
using StickerSnap.Core.Links;
using StickerSnap.Core.Settings;

namespace StickerSnap.Tests.Application.Links;

/// <summary>Sites whose downloads the test ends by hand, each call handed out as it comes.</summary>
internal sealed class ControlledSites : IVideoSites
{
    private readonly Channel<SiteCall> _calls = Channel.CreateUnbounded<SiteCall>();

    public int Probes { get; private set; }

    public Func<VideoLink, LinkVideo> Probe { get; set; } = link => Videos.Of(link.Url);

    public Task<LinkVideo> ProbeAsync(VideoLink link, CancellationToken cancellationToken)
    {
        Probes++;
        return Task.FromResult(Probe(link));
    }

    public Task<long> DownloadAsync(
        LinkVideo video, string path, Action<DownloadProgress> progress, CancellationToken cancellationToken)
    {
        var call = new SiteCall(video, path, progress, cancellationToken);
        _calls.Writer.TryWrite(call);
        return call.Task;
    }

    /// <summary>Whether a download was asked for that the test has not taken yet.</summary>
    public bool HasNext => _calls.Reader.TryPeek(out _);

    /// <summary>The next download asked for; a guard of 10 s fails a test that hangs.</summary>
    public async Task<SiteCall> NextAsync()
    {
        using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        guard.CancelAfter(TimeSpan.FromSeconds(10));
        return await _calls.Reader.ReadAsync(guard.Token);
    }
}

/// <summary>One download the test is running for the service.</summary>
internal sealed class SiteCall
{
    private readonly TaskCompletionSource<long> _end = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public SiteCall(LinkVideo video, string path, Action<DownloadProgress> progress, CancellationToken cancellationToken)
    {
        Video = video;
        Path = path;
        Progress = progress;
        Token = cancellationToken;
        cancellationToken.Register(() => _end.TrySetCanceled(cancellationToken));
    }

    public LinkVideo Video { get; }

    public string Path { get; }

    public Action<DownloadProgress> Progress { get; }

    public CancellationToken Token { get; }

    public Task<long> Task => _end.Task;

    public void Complete(long bytes) => _end.TrySetResult(bytes);

    public void Throw(Exception exception) => _end.TrySetException(exception);
}

internal static class Videos
{
    public static LinkVideo Of(string url, long bytes = 38_000_000) =>
        new(LinkSite.Youtube, url, "Кот смотрит в окно", 48, null, bytes, "720p", "247", "webm");
}

/// <summary>The downloadUpdated events sent, and a way to await one.</summary>
internal sealed class DownloadEvents : IEventPublisher
{
    private readonly Lock _gate = new();
    private readonly List<Download> _sent = [];
    private readonly List<(Func<Download, bool> Match, TaskCompletionSource<Download> Found)> _awaited = [];

    public IReadOnlyList<Download> Sent(string id)
    {
        lock (_gate) return [.. _sent.Where(download => download.Id == id)];
    }

    /// <summary>The first download sent that matches, now or later; a guard of 10 s fails a test that hangs.</summary>
    public Task<Download> UntilAsync(string id, DownloadState state)
    {
        lock (_gate)
        {
            if (_sent.FirstOrDefault(sent => sent.Id == id && sent.State == state) is { } sent) return Task.FromResult(sent);
            var found = new TaskCompletionSource<Download>(TaskCreationOptions.RunContinuationsAsynchronously);
            _awaited.Add((download => download.Id == id && download.State == state, found));
            return found.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
    }

    public Task DownloadUpdatedAsync(Download download, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _sent.Add(download);
            foreach (var awaited in _awaited.Where(awaited => awaited.Match(download)).ToList())
            {
                awaited.Found.TrySetResult(download);
                _awaited.Remove(awaited);
            }
        }
        return Task.CompletedTask;
    }

    public Task SettingsChangedAsync(UserSettings settings, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ProjectChangedAsync(ProjectListItem project, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ProjectRemovedAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task JobUpdatedAsync(StickerSnap.Core.Jobs.Job job, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task AccountChangedAsync(StickerSnap.Core.Account.AccountLink link, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task PackChangedAsync(StickerSnap.Core.Packs.Pack pack, CancellationToken cancellationToken) => Task.CompletedTask;
}
