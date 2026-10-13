using StickerSnap.Application.Exports;
using StickerSnap.Infrastructure.Files;

namespace StickerSnap.Tests.Infrastructure.Files;

/// <summary>
/// The stickers folder: a sticker is rendered beside its place and takes the first free name, or
/// the name of its own last export, never another file's.
/// </summary>
public sealed class StickerFolderTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("stickersnap-folder-");
    private readonly StickerFolder _folder = new();

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    public void Dispose() => _root.Delete(recursive: true);

    private string Folder => Path.Combine(_root.FullName, "Стикеры");

    private static IEnumerable<string> Names => StickerFileNames.Candidates("Кот", "Стикер 1");

    private async Task<StickerSlot> RenderedAsync(byte content)
    {
        var slot = await _folder.ReserveAsync(Folder, Cancel);
        await File.WriteAllBytesAsync(slot.TemporaryPath, [content], Cancel);
        return slot;
    }

    private async Task<string> ExistingAsync(string name, byte content)
    {
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, name);
        await File.WriteAllBytesAsync(path, [content], Cancel);
        return path;
    }

    [Fact]
    public async Task Renders_beside_the_place_in_a_folder_it_creates()
    {
        var slot = await _folder.ReserveAsync(Folder, Cancel);

        Directory.Exists(Folder).Should().BeTrue();
        Path.GetDirectoryName(slot.TemporaryPath).Should().Be(Folder);
        Path.GetFileName(slot.TemporaryPath).Should().StartWith(".").And.EndWith(".part.webm");
    }

    [Fact]
    public async Task Takes_the_name_of_project_and_segment()
    {
        var slot = await RenderedAsync(1);

        var path = await _folder.KeepAsync(slot, Names, replacing: null, Cancel);

        path.Should().Be(Path.Combine(Folder, "Кот — Стикер 1.webm"));
        (await File.ReadAllBytesAsync(path, Cancel)).Should().Equal(1);
        File.Exists(slot.TemporaryPath).Should().BeFalse();
    }

    [Fact]
    public async Task Leaves_another_file_of_that_name_and_takes_the_next()
    {
        var other = await ExistingAsync("Кот — Стикер 1.webm", 7);

        var path = await _folder.KeepAsync(await RenderedAsync(1), Names, replacing: null, Cancel);

        path.Should().Be(Path.Combine(Folder, "Кот — Стикер 1 (2).webm"));
        (await File.ReadAllBytesAsync(other, Cancel)).Should().Equal(7);
    }

    [Fact]
    public async Task Replaces_the_segment_s_own_last_export()
    {
        await ExistingAsync("Кот — Стикер 1.webm", 7);
        var last = await ExistingAsync("Кот — Стикер 1 (2).webm", 8);

        var path = await _folder.KeepAsync(await RenderedAsync(1), Names, replacing: last, Cancel);

        path.Should().Be(last);
        (await File.ReadAllBytesAsync(path, Cancel)).Should().Equal(1);
        Directory.EnumerateFiles(Folder).Should().HaveCount(2);
    }

    [Fact]
    public async Task Discards_a_render_that_did_not_go()
    {
        var slot = await RenderedAsync(1);

        _folder.Discard(slot);
        _folder.Discard(slot);

        Directory.EnumerateFiles(Folder).Should().BeEmpty();
    }

    [Fact]
    public void Names_a_sticker_safely_whatever_its_project_and_segment_are_called()
    {
        StickerFileNames.Candidates("Кот: 2/3", " Стикер?1 ").Take(2)
            .Should().Equal("Кот_ 2_3 — Стикер_1.webm", "Кот_ 2_3 — Стикер_1 (2).webm");
    }
}
