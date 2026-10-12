namespace StickerSnap.Core.Projects;

/// <summary>A project as its .ssproj keeps it: the scene, the source it cuts, and its place in the list.</summary>
/// <param name="Id">The same on every machine the file is opened on (G6): packs refer to the project by it</param>
/// <param name="Name">Shown in the list; renaming changes it, not the file's name</param>
/// <param name="Revision">Grows with every save of the scene, so a stale save is caught</param>
public sealed record Project(Guid Id, string Name, int Revision, DateTimeOffset SavedAt, ProjectSource Source, Scene Scene);
