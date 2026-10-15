namespace StickerSnap.Core.Settings;

/// <summary>
/// The user's settings (contract, Settings): the look, the two folders, and the emoji last
/// published with, which the server keeps and the user does not edit.
/// </summary>
public sealed record UserSettings(Theme Theme, Accent Accent, Folders Folders, IReadOnlyList<string> RecentEmoji)
{
    /// <summary>A first launch: the system's theme, indigo, no emoji yet.</summary>
    public static UserSettings Default(Folders folders) => new(Theme.System, Accent.Indigo, folders, []);

    /// <summary>How many emoji «Недавние» of the emoji picker keeps.</summary>
    public const int MaxRecentEmoji = 24;

    /// <summary>
    /// After a publication: <paramref name="used"/> first, in its order, then the emoji
    /// known before, each once, at most <see cref="MaxRecentEmoji"/>.
    /// </summary>
    public UserSettings WithRecentEmoji(IEnumerable<string> used) => this with
    {
        RecentEmoji = [.. used.Concat(RecentEmoji).Distinct(StringComparer.Ordinal).Take(MaxRecentEmoji)],
    };

    /// <summary>These settings with the fields <paramref name="change"/> names replaced.</summary>
    public UserSettings With(SettingsChange change) => this with
    {
        Theme = change.Theme ?? Theme,
        Accent = change.Accent ?? Accent,
        Folders = new Folders(change.ProjectsFolder ?? Folders.Projects, change.StickersFolder ?? Folders.Stickers),
    };
}
