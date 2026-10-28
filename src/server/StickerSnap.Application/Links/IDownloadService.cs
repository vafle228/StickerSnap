using StickerSnap.Core.Errors;
using StickerSnap.Core.Links;

namespace StickerSnap.Application.Links;

/// <summary>
/// /api/v1/downloads (artboards 20·3·5–20·4): a video by link is downloaded and becomes a new
/// project. Downloads live in memory; each change goes out as downloadUpdated.
/// </summary>
public interface IDownloadService
{
    /// <summary>
    /// A new download of the video of <paramref name="url"/>, queued behind the one running. The
    /// link is checked and its video probed first, so a bad link is answered at once.
    /// </summary>
    /// <exception cref="AppException">what <see cref="ILinkService.ProbeAsync"/> throws</exception>
    Task<Download> CreateAsync(string url, CancellationToken cancellationToken);

    /// <summary>The downloads running, waiting and finished in the last 10 minutes, newest first: after F5.</summary>
    IReadOnlyList<Download> List();

    /// <summary>«Отменить» (20·3·9): the file downloaded so far is removed; a finished download is answered as it is.</summary>
    /// <exception cref="AppException">job_not_found</exception>
    Download Cancel(string id);
}
