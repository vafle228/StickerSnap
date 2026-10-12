using StickerSnap.Core.Settings;
using StickerSnap.Infrastructure.Settings;

namespace StickerSnap.Tests.Infrastructure.Settings;

/// <summary>The folders of a first launch, by the paths the OS gives.</summary>
public sealed class DefaultFoldersTests
{
    [Fact]
    public void Lives_in_documents_on_windows()
    {
        var folders = DefaultFolders.For(windows: true, documents: @"C:\Users\u\Documents", home: @"C:\Users\u");

        var root = Path.Combine(@"C:\Users\u\Documents", "StickerSnap");
        folders.Should().Be(new Folders(Path.Combine(root, "Проекты"), Path.Combine(root, "Стикеры")));
    }

    [Fact]
    public void Lives_in_the_home_directory_on_linux()
    {
        var folders = DefaultFolders.For(windows: false, documents: "/home/u/Documents", home: "/home/u");

        var root = Path.Combine("/home/u", "StickerSnap");
        folders.Should().Be(new Folders(Path.Combine(root, "Проекты"), Path.Combine(root, "Стикеры")));
    }

    [Fact]
    public void Gives_absolute_paths_on_this_machine()
    {
        var folders = DefaultFolders.Current();

        Path.IsPathFullyQualified(folders.Projects).Should().BeTrue();
        Path.IsPathFullyQualified(folders.Stickers).Should().BeTrue();
    }
}
