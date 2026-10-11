using StickerSnap.Core.Settings;

namespace StickerSnap.Infrastructure.Settings;

/// <summary>
/// The folders of a first launch (plan, R22): Documents\StickerSnap\Проекты and …\Стикеры on
/// Windows, ~/StickerSnap/Проекты and …/Стикеры on Linux. Nothing is created until it is used.
/// </summary>
internal static class DefaultFolders
{
    public static Folders Current() => For(
        OperatingSystem.IsWindows(),
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    internal static Folders For(bool windows, string documents, string home)
    {
        var root = Path.Combine(windows ? documents : home, "StickerSnap");
        return new Folders(Path.Combine(root, "Проекты"), Path.Combine(root, "Стикеры"));
    }
}
