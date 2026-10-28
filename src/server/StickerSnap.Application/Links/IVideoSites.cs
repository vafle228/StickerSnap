using StickerSnap.Core.Errors;
using StickerSnap.Core.Links;

namespace StickerSnap.Application.Links;

/// <summary>
/// The sites videos are imported from by link (YouTube, Giphy): what a video is, and its file.
/// How a site is asked is the adapter's business; the use cases see only links and files.
/// </summary>
public interface IVideoSites
{
    /// <summary>
    /// What the site says of the video, and which of its files would be downloaded: the best
    /// MP4 or WEBM up to 720p without sound; a Giphy gif's MP4, a Giphy sticker's WEBP.
    /// </summary>
    /// <exception cref="AppException">
    /// video_unavailable (`params.reason`: gone, live, adult); link_unsupported (site) for a page
    /// that is not a video; site_unreachable (`params.site`); site_changed when the site's answer
    /// cannot be understood
    /// </exception>
    Task<LinkVideo> ProbeAsync(VideoLink link, CancellationToken cancellationToken);

    /// <summary>
    /// Downloads the file <see cref="ProbeAsync"/> picked to <paramref name="path"/>, telling how
    /// far it got as it goes. Cancelling stops it; what was written by then is the caller's to remove.
    /// </summary>
    /// <returns>How big the file is</returns>
    /// <exception cref="AppException">what <see cref="ProbeAsync"/> throws; disk_full, save_failed</exception>
    Task<long> DownloadAsync(
        LinkVideo video, string path, Action<DownloadProgress> progress, CancellationToken cancellationToken);
}

/// <param name="EtaSec">How long is left, when the site tells</param>
public sealed record DownloadProgress(long ReceivedBytes, double? EtaSec);
