namespace StickerSnap.Application.App;

/// <summary>What the host knows about this launch: the build and who started it.</summary>
public interface IHostInfo
{
    /// <summary>The app's version, "0.1.0"</summary>
    string AppVersion { get; }

    AppMode Mode { get; }

    /// <summary>The desktop shell and its version, "Electron 33.2.1"; null in the browser</summary>
    string? ShellVersion { get; }
}
