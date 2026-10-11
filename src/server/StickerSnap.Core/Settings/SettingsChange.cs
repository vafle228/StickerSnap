namespace StickerSnap.Core.Settings;

/// <summary>What PATCH /api/v1/settings changes: any of the fields, null for the ones it keeps.</summary>
public sealed record SettingsChange(
    Theme? Theme = null,
    Accent? Accent = null,
    string? ProjectsFolder = null,
    string? StickersFolder = null)
{
    public bool IsEmpty => Theme is null && Accent is null && ProjectsFolder is null && StickersFolder is null;
}
