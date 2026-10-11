namespace StickerSnap.Infrastructure.Storage;

/// <summary>Where the server keeps its files: StickerSnap__DataDir.</summary>
public sealed class DataOptions
{
    public const string Section = "StickerSnap";

    /// <summary>
    /// The registries of projects and packs, the settings, the secrets. By default the user's
    /// application data, the same folder Electron keeps its own in: %APPDATA%\StickerSnap on
    /// Windows, ~/.config/StickerSnap on Linux.
    /// </summary>
    public string DataDir { get; set; } = DefaultDataDir();

    internal static string DefaultDataDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StickerSnap");
}
