using System.Text.Json.Serialization;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Settings;

namespace StickerSnap.Api.Contracts;

/// <summary>GET and PATCH /api/v1/settings, event settingsChanged (schema Settings).</summary>
public sealed record Settings(Theme Theme, Accent Accent, Folders Folders, IReadOnlyList<string> RecentEmoji)
{
    public static Settings From(UserSettings settings) => new(
        settings.Theme,
        settings.Accent,
        new Folders(settings.Folders.Projects, settings.Folders.Stickers),
        settings.RecentEmoji);
}

public sealed record Folders(string Projects, string Stickers);

/// <summary>
/// PATCH /api/v1/settings (schema SettingsPatch): at least one field; recentEmoji is the server's.
/// A field the schema does not have is refused rather than silently dropped.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SettingsPatch(Theme? Theme, Accent? Accent, FoldersPatch? Folders)
{
    public SettingsChange ToChange()
    {
        if (Folders is { Projects: null, Stickers: null })
            throw new AppException(ErrorCode.BadRequest, "folders names no folder")
            {
                Fields = new Dictionary<string, string[]> { ["folders"] = ["empty"] },
            };
        return new SettingsChange(Theme, Accent, Folders?.Projects, Folders?.Stickers);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FoldersPatch(string? Projects, string? Stickers);
