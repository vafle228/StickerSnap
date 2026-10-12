using StickerSnap.Application.Media;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;

namespace StickerSnap.Application.Sources;

/// <summary>
/// Reads the video a new project is made from, or that replaces a missing one: the media engine
/// reads it, an upload is taken for good, and its fingerprint is kept with what was read.
/// </summary>
public sealed class SourceImport(SourceFiles files, IMediaProbe probe, IFileFingerprint fingerprints, IUploadStore uploads)
{
    /// <summary>
    /// The source a project keeps. Cancelling stops the reading; once an upload is taken, the
    /// reading is over and is no longer cancelled, so a taken upload always ends in the project.
    /// </summary>
    /// <exception cref="AppException">
    /// what <see cref="SourceFiles.PathOf"/> and <see cref="IMediaProbe.ProbeAsync"/> throw;
    /// source_unreadable for a file without a single frame
    /// </exception>
    public async Task<ProjectSource> ReadAsync(SourceRef source, CancellationToken cancellationToken)
    {
        var path = files.PathOf(source);
        var media = await probe.ProbeAsync(path, cancellationToken);
        if (media.FrameCount < 1)
            throw new AppException(ErrorCode.SourceUnreadable, $"{Path.GetFileName(path)} has no frames")
            {
                Log = $"{Path.GetFileName(path)}: no video frames",
            };

        var fingerprint = await fingerprints.OfAsync(path, cancellationToken);
        if (source is SourceRef.Upload upload) path = await uploads.KeepAsync(upload.UploadId, cancellationToken);

        return new ProjectSource(path, Path.GetFileName(path), fingerprint, media);
    }
}
