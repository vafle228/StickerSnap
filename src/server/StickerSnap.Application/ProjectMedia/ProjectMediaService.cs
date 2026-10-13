using StickerSnap.Application.Media;
using StickerSnap.Application.Projects;
using StickerSnap.Core.Errors;
using StickerSnap.Core.Media;
using StickerSnap.Core.Projects;

namespace StickerSnap.Application.ProjectMedia;

/// <summary>
/// <see cref="IProjectMediaService"/> over <see cref="IEditorMedia"/> and <see cref="IMediaCache"/>.
/// Every file is keyed by the source's fingerprint, so files of the same video are shared between
/// projects, and a replaced video gets its own.
/// </summary>
public sealed class ProjectMediaService(IProjectService projects, IEditorMedia media, IMediaCache cache) : IProjectMediaService
{
    private const string Webm = "video/webm";
    private const string Webp = "image/webp";

    /// <inheritdoc />
    public async Task<MediaFile> VideoAsync(Guid id, CancellationToken cancellationToken)
    {
        var source = (await WithSourceAsync(id, cancellationToken)).Project.Source;
        if (BrowserPlayback.PlaysAsIs(source.Media))
            return new MediaFile(source.Path, BrowserPlayback.ContentTypeOf(source.Media.Format), MediaKeys.Of("source", source.Fingerprint));

        var key = MediaKeys.Of("playable", source.Fingerprint);
        var path = await cache.GetOrAddAsync(
            key, "webm", (output, ct) => media.WritePlayableAsync(source.Path, source.Media, output, ct), cancellationToken);
        return new MediaFile(path, Webm, key);
    }

    /// <inheritdoc />
    public async Task<MediaFile> CoverAsync(Guid id, CancellationToken cancellationToken)
    {
        var opened = await projects.GetAsync(id, cancellationToken);
        var source = opened.Project.Source;
        var cover = CoverShot.Of(opened.Project);
        var key = MediaKeys.Of("cover", source.Fingerprint, cover.Frame, cover.Crop, cover.Size);
        if (cache.Find(key, "webp") is { } kept) return new MediaFile(kept, Webp, key);

        RequireSource(opened);
        var path = await cache.GetOrAddAsync(
            key, "webp", (output, ct) => media.WriteCoverAsync(source.Path, source.Media, cover, output, ct), cancellationToken);
        return new MediaFile(path, Webp, key);
    }

    /// <inheritdoc />
    public async Task<MediaFile> FramesAsync(Guid id, int from, int count, int height, CancellationToken cancellationToken)
    {
        var source = (await WithSourceAsync(id, cancellationToken)).Project.Source;
        var strip = FrameStrip.Of(source.Media, from, count, height);
        var key = MediaKeys.Of("frames", source.Fingerprint, strip.From, strip.Count, strip.Height);
        var path = await cache.GetOrAddAsync(
            key, "webp", (output, ct) => media.WriteStripAsync(source.Path, source.Media, strip, output, ct), cancellationToken);
        return new MediaFile(path, Webp, key);
    }

    private async Task<OpenedProject> WithSourceAsync(Guid id, CancellationToken cancellationToken)
    {
        var opened = await projects.GetAsync(id, cancellationToken);
        RequireSource(opened);
        return opened;
    }

    private static void RequireSource(OpenedProject opened)
    {
        if (opened.SourceStatus == PresenceStatus.Missing)
            throw new AppException(ErrorCode.SourceNotFound, $"The video of project {opened.Project.Id} is not at {opened.Project.Source.Path}");
    }
}
