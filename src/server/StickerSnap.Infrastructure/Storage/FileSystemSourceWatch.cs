using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using StickerSnap.Application.Projects;

namespace StickerSnap.Infrastructure.Storage;

/// <summary>
/// One FileSystemWatcher per project whose source is missing, on the folder the source was in,
/// for its name only. A file created there or renamed to that name ends the watch.
/// </summary>
internal sealed class FileSystemSourceWatch(ILogger<FileSystemSourceWatch> logger) : ISourceWatch, IDisposable
{
    private readonly ConcurrentDictionary<Guid, FileSystemWatcher> _watchers = new();

    public void Watch(Guid projectId, string path, Func<Task> appeared)
    {
        var folder = Path.GetDirectoryName(path);
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(name) || !Directory.Exists(folder))
        {
            Forget(projectId);
            return;
        }

        FileSystemWatcher watcher;
        try
        {
            watcher = new FileSystemWatcher(folder, name) { NotifyFilter = NotifyFilters.FileName };
        }
        catch (Exception exception) when (IoErrors.IsIoError(exception))
        {
            logger.LogWarning(exception, "Cannot watch {Folder} for the source of project {Id}", folder, projectId);
            return;
        }

        var fired = 0;
        void OnChange(object sender, FileSystemEventArgs change)
        {
            if (!File.Exists(path) || Interlocked.Exchange(ref fired, 1) == 1) return;
            if (_watchers.TryRemove(new KeyValuePair<Guid, FileSystemWatcher>(projectId, watcher))) watcher.Dispose();
            _ = TellAsync(projectId, appeared);
        }

        watcher.Created += OnChange;
        watcher.Renamed += OnChange;
        _watchers.AddOrUpdate(projectId, watcher, (_, old) =>
        {
            old.Dispose();
            return watcher;
        });
        watcher.EnableRaisingEvents = true;

        // The file may have come back between the check that found it missing and the watch
        if (File.Exists(path)) OnChange(watcher, new FileSystemEventArgs(WatcherChangeTypes.Created, folder, name));
    }

    public void Forget(Guid projectId)
    {
        if (_watchers.TryRemove(projectId, out var watcher)) watcher.Dispose();
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers.Values) watcher.Dispose();
        _watchers.Clear();
    }

    private async Task TellAsync(Guid projectId, Func<Task> appeared)
    {
        try
        {
            await appeared();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Telling that the source of project {Id} is back failed", projectId);
        }
    }
}
