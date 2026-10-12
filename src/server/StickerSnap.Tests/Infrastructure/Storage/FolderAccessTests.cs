using StickerSnap.Core.Errors;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Tests.Infrastructure.Storage;

/// <summary>A chosen folder is created and proven writable, and keeps nothing of the proof.</summary>
public sealed class FolderAccessTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("stickersnap-folders-");

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public void Dispose() => _root.Delete(recursive: true);

    [Fact]
    public async Task Creates_a_missing_folder_with_its_parents()
    {
        var folder = Path.Combine(_root.FullName, "StickerSnap", "Стикеры");

        await new FolderAccess().PrepareAsync(folder, Cancel);

        Directory.Exists(folder).Should().BeTrue();
    }

    [Fact]
    public async Task Leaves_no_probe_file_behind()
    {
        await new FolderAccess().PrepareAsync(_root.FullName, Cancel);

        _root.GetFileSystemInfos().Should().BeEmpty();
    }

    [Fact]
    public async Task Fails_to_save_into_a_path_taken_by_a_file()
    {
        var file = Path.Combine(_root.FullName, "not-a-folder");
        await File.WriteAllTextAsync(file, "", Cancel);

        var prepare = () => new FolderAccess().PrepareAsync(file, Cancel);

        (await prepare.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCode.SaveFailed);
    }
}
