using Microsoft.Extensions.Logging.Abstractions;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Tests.Infrastructure.Storage;

/// <summary>Waiting for a missing source in a temporary folder: the system tells, the watch calls back once.</summary>
public sealed class FileSystemSourceWatchTests : IDisposable
{
    /// <summary>How long the system may take to tell about a file; the test ends as soon as it does.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("stickersnap-watch-");
    private readonly FileSystemSourceWatch _watch = new(NullLogger<FileSystemSourceWatch>.Instance);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private string PathOf(string name) => Path.Combine(_folder.FullName, name);

    public void Dispose()
    {
        _watch.Dispose();
        _folder.Delete(recursive: true);
    }

    private (Task Appeared, Func<int> Calls) Watch(Guid id, string path)
    {
        var appeared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        _watch.Watch(id, path, () =>
        {
            Interlocked.Increment(ref calls);
            appeared.TrySetResult();
            return Task.CompletedTask;
        });
        return (appeared.Task, () => Volatile.Read(ref calls));
    }

    [Fact]
    public async Task Calls_back_once_when_the_file_is_put_back()
    {
        var path = PathOf("cat.gif");
        var (appeared, calls) = Watch(Guid.NewGuid(), path);

        await File.WriteAllBytesAsync(path, [1], Cancel);
        await appeared.WaitAsync(Patience, Cancel);
        File.Move(path, PathOf("other.gif"));
        File.Move(PathOf("other.gif"), path);

        calls().Should().Be(1);
    }

    [Fact]
    public async Task Calls_back_when_a_file_is_renamed_to_the_source_s_name()
    {
        var path = PathOf("cat.gif");
        await File.WriteAllBytesAsync(PathOf("cat.gif.part"), [1], Cancel);
        var (appeared, _) = Watch(Guid.NewGuid(), path);

        File.Move(PathOf("cat.gif.part"), path);

        await appeared.WaitAsync(Patience, Cancel);
    }

    [Fact]
    public async Task Calls_back_at_once_for_a_file_that_came_back_before_the_watch()
    {
        var path = PathOf("cat.gif");
        await File.WriteAllBytesAsync(path, [1], Cancel);

        var (appeared, _) = Watch(Guid.NewGuid(), path);

        appeared.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task Is_silent_about_a_forgotten_project_and_other_files()
    {
        var id = Guid.NewGuid();
        var (forgotten, _) = Watch(id, PathOf("cat.gif"));
        _watch.Forget(id);
        var (other, _) = Watch(Guid.NewGuid(), PathOf("dog.gif"));

        await File.WriteAllBytesAsync(PathOf("cat.gif"), [1], Cancel);
        await File.WriteAllBytesAsync(PathOf("dog.gif"), [1], Cancel);
        await other.WaitAsync(Patience, Cancel);

        forgotten.IsCompleted.Should().BeFalse();
    }

    [Fact]
    public void Does_not_watch_a_folder_that_is_gone()
    {
        var watch = () => Watch(Guid.NewGuid(), Path.Combine(_folder.FullName, "gone", "cat.gif"));

        watch.Should().NotThrow();
    }
}
