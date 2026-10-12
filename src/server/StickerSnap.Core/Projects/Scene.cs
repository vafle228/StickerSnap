namespace StickerSnap.Core.Projects;

/// <summary>
/// What the editor edits and the .ssproj keeps (contract, Scene): the layers the source is drawn
/// on and the stickers cut from it. Everything else about a project the server works out itself.
/// </summary>
/// <param name="SchemaVersion">The contract's version of the scene; only <see cref="CurrentSchemaVersion"/> is known</param>
/// <param name="Layers">In the MVP exactly one, the video</param>
/// <param name="Segments">R18: several stickers of one source; none once the user deletes the last</param>
public sealed record Scene(int SchemaVersion, IReadOnlyList<Layer> Layers, IReadOnlyList<Segment> Segments)
{
    public const int CurrentSchemaVersion = 1;
}
