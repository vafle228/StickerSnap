namespace StickerSnap.Core.Projects;

/// <summary>A project as its .ssproj keeps it: the scene, the source it cuts, and its place in the list.</summary>
/// <param name="Id">The same on every machine the file is opened on (G6): packs refer to the project by it</param>
/// <param name="Name">Shown in the list; renaming changes it, not the file's name</param>
/// <param name="Revision">Grows with every save of the scene, so a stale save is caught; an export leaves it (G7)</param>
/// <param name="Exports">The last export of each segment, by the segment's id; none when omitted</param>
public sealed record Project(
    Guid Id,
    string Name,
    int Revision,
    DateTimeOffset SavedAt,
    ProjectSource Source,
    Scene Scene,
    IReadOnlyDictionary<string, SegmentExport>? Exports = null)
{
    private static readonly IReadOnlyDictionary<string, SegmentExport> None = new Dictionary<string, SegmentExport>();

    public IReadOnlyDictionary<string, SegmentExport> Exports { get; init; } = Exports ?? None;

    /// <summary>The project with <paramref name="export"/> as the last export of its segment; the revision stays.</summary>
    public Project WithExport(SegmentExport export) => this with
    {
        Exports = new Dictionary<string, SegmentExport>(Exports, StringComparer.Ordinal) { [export.SegmentId] = export },
    };
}
