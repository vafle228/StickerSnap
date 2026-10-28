using Microsoft.Extensions.Time.Testing;
using StickerSnap.Infrastructure.Storage;

namespace StickerSnap.Tests.Infrastructure.Storage;

/// <summary>
/// «Удалить с диска»: the FreeDesktop and the macOS trash in a temporary home on any system, the Windows recycle
/// bin for real on Windows only.
/// </summary>
public sealed class TrashTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("stickersnap-trash-");
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 12, 12, 30, 5, TimeSpan.Zero));

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private string TrashDir => Path.Combine(_root.FullName, "Trash");

    public TrashTests() => _clock.SetLocalTimeZone(TimeZoneInfo.Utc);

    public void Dispose() => _root.Delete(recursive: true);

    private async Task<string> ProjectFileAsync(string folder, string name = "кот и клавиатура.ssproj")
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root.FullName, folder)).FullName;
        var path = Path.Combine(directory, name);
        await File.WriteAllTextAsync(path, "{}", Cancel);
        return path;
    }

    [Fact]
    public async Task Moves_the_file_into_the_freedesktop_trash_with_where_it_came_from()
    {
        var path = await ProjectFileAsync("Проекты");

        await new FreedesktopTrash(TrashDir, _clock).MoveToTrashAsync(path, Cancel);

        File.Exists(path).Should().BeFalse();
        File.Exists(Path.Combine(TrashDir, "files", "кот и клавиатура.ssproj")).Should().BeTrue();
        var info = await File.ReadAllTextAsync(Path.Combine(TrashDir, "info", "кот и клавиатура.ssproj.trashinfo"), Cancel);
        info.Should().StartWith("[Trash Info]\n");
        info.Should().Contain("DeletionDate=2026-10-12T12:30:05\n");
        info.Should().Contain("%D0%BA%D0%BE%D1%82%20%D0%B8%20");
    }

    [Fact]
    public async Task Gives_a_second_file_of_the_same_name_a_name_of_its_own()
    {
        var trash = new FreedesktopTrash(TrashDir, _clock);
        await trash.MoveToTrashAsync(await ProjectFileAsync("a", "cat.ssproj"), Cancel);

        await trash.MoveToTrashAsync(await ProjectFileAsync("b", "cat.ssproj"), Cancel);

        Directory.GetFiles(Path.Combine(TrashDir, "files")).Select(Path.GetFileName)
            .Should().BeEquivalentTo(["cat.ssproj", "cat.2.ssproj"]);
        File.Exists(Path.Combine(TrashDir, "info", "cat.2.ssproj.trashinfo")).Should().BeTrue();
    }

    [Fact]
    public async Task Has_nothing_to_do_for_a_file_that_is_already_gone()
    {
        var gone = Path.Combine(_root.FullName, "gone.ssproj");

        await new FreedesktopTrash(TrashDir, _clock).MoveToTrashAsync(gone, Cancel);

        Directory.Exists(TrashDir).Should().BeFalse();
    }

    [Fact]
    public async Task Moves_the_file_into_the_mac_trash_and_names_a_second_one_as_finder_does()
    {
        var trash = new MacTrash(TrashDir);
        await trash.MoveToTrashAsync(await ProjectFileAsync("a", "cat.ssproj"), Cancel);
        var second = await ProjectFileAsync("b", "cat.ssproj");

        await trash.MoveToTrashAsync(second, Cancel);

        File.Exists(second).Should().BeFalse();
        Directory.GetFiles(TrashDir).Select(Path.GetFileName)
            .Should().BeEquivalentTo(["cat.ssproj", "cat 2.ssproj"]);
    }

    [Fact]
    public async Task Has_nothing_to_move_into_the_mac_trash_for_a_file_that_is_already_gone()
    {
        await new MacTrash(TrashDir).MoveToTrashAsync(Path.Combine(_root.FullName, "gone.ssproj"), Cancel);

        Directory.Exists(TrashDir).Should().BeFalse();
    }

    [Fact]
    public async Task Sends_the_file_to_the_windows_recycle_bin()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The recycle bin is Windows' own");
        var path = await ProjectFileAsync("Проекты", $"stickersnap-test-{Guid.NewGuid():N}.ssproj");

#pragma warning disable CA1416 // Skipped above on every other system
        await new WindowsRecycleBin().MoveToTrashAsync(path, Cancel);
#pragma warning restore CA1416

        File.Exists(path).Should().BeFalse();
    }
}
