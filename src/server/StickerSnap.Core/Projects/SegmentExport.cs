using StickerSnap.Core.Render;
using StickerSnap.Core.Validation;

namespace StickerSnap.Core.Projects;

/// <summary>
/// The last export of a segment, as the project's file keeps it (contract, ExportResult): where
/// the sticker went, what it came out as, and what the segment was when it was made, so an edit
/// after it shows as «устарел» while other segments' exports stay fresh.
/// </summary>
/// <param name="SceneRevision">The saved revision the export was asked for</param>
/// <param name="Signature">
/// <see cref="SegmentSignature"/> of the segment and the source it was cut from, as exported
/// </param>
/// <param name="FileId">How GET /api/v1/files knows the file</param>
/// <param name="Path">The .webm in the stickers folder, absolute</param>
/// <param name="Bytes">Of the file</param>
/// <param name="Crf">The quality the sticker is held to: the CRF of the step taken</param>
/// <param name="Concessions">«Чем пожертвовали»; empty when nothing was given up</param>
/// <param name="Ladder">«Как подбирали»: the steps tried, the one taken last</param>
/// <param name="Validation">Telegram's checklist of the file</param>
public sealed record SegmentExport(
    string SegmentId,
    int SceneRevision,
    string Signature,
    string FileId,
    string Path,
    long Bytes,
    StickerOutput Output,
    int Crf,
    IReadOnlyList<Concession> Concessions,
    IReadOnlyList<LadderTry> Ladder,
    StickerValidation Validation)
{
    /// <summary>The file's name, as the checklist names it.</summary>
    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>
    /// The segment was edited after the export, removed, or the project took another video: the
    /// file no longer is what the segment would give. A save of other segments changes nothing.
    /// </summary>
    public bool IsStaleIn(Project project) =>
        project.Scene.Segments.FirstOrDefault(segment => segment.Id == SegmentId) is not { } segment ||
        SegmentSignature.Of(segment, project.Source) != Signature;
}
