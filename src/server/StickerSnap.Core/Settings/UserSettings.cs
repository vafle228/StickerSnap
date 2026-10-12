namespace StickerSnap.Core.Settings;

/// <summary>
/// The user's settings (contract, Settings): the look, the two folders, and the emoji last
/// published with, which the server keeps and the user does not edit.
/// </summary>
public sealed record UserSettings(Theme Theme, Accent Accent, Folders Folders, IReadOnlyList<string> RecentEmoji)
{
    /// <summary>A first launch: the system's theme, indigo, no emoji yet.</summary>
    public static UserSettings Default(Folders folders) => new(Theme.System, Accent.Indigo, folders, []);

    /// <summary>These settings with the fields <paramref name="change"/> names replaced.</summary>
    public UserSettings With(SettingsChange change) => this with
    {
        Theme = change.Theme ?? Theme,
        Accent = change.Accent ?? Accent,
        Folders = new Folders(change.ProjectsFolder ?? Folders.Projects, change.StickersFolder ?? Folders.Stickers),
    };
}
