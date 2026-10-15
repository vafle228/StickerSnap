using StickerSnap.Core.Media;
using StickerSnap.Core.Render;

namespace StickerSnap.Core.Projects;

/// <summary>
/// What the «Проекты» grid shows of a project without opening its file: the registry keeps one
/// per project and refreshes it on every save, so the list is read from one small file.
/// </summary>
/// <param name="Path">The .ssproj, absolute</param>
/// <param name="SourcePath">The video it was cut from, to tell whether it is still there</param>
/// <param name="UpdatedAt">The project's last save: the list is sorted by it, newest first</param>
/// <param name="DurationSec">How long the longest segment plays, for the chip on the cover; 0 without segments</param>
public sealed record ProjectCard(
    Guid Id,
    string Name,
    string Path,
    string SourcePath,
    DateTimeOffset UpdatedAt,
    int SegmentCount,
    CardPreset Preset,
    double DurationSec)
{
    public static ProjectCard Of(Project project, string path) => new(
        project.Id,
        project.Name,
        path,
        project.Source.Path,
        project.SavedAt,
        project.Scene.Segments.Count,
        PresetOf(project.Scene.Segments),
        project.Scene.Segments.Select(segment => segment.PlaySeconds(project.Source)).DefaultIfEmpty(0).Max());

    /// <summary>«3 стикера», «5 эмодзи», or a mix of both; a project without segments counts as stickers.</summary>
    private static CardPreset PresetOf(IReadOnlyList<Segment> segments) =>
        segments.Select(segment => segment.Preset).Distinct().ToList() switch
        {
            [SegmentPreset.Emoji] => CardPreset.Emoji,
            [_, _, ..] => CardPreset.Mixed,
            _ => CardPreset.Sticker,
        };
}

/// <summary>What a card says the project makes (contract, ProjectSummary.preset).</summary>
public enum CardPreset
{
    Sticker,
    Emoji,
    Mixed,
}
